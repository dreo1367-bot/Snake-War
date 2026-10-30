using OpenTK.Mathematics;

namespace BrickBreaker3D.Physics;

/// <summary>Один контакт (точка соприкосновения) двух тел.</summary>
public struct Contact
{
    public PhysicsObject A;
    public PhysicsObject B;      // null => контакт с бесконечной плоскостью пола
    public Vector3 Normal;       // мировая нормаль: толкает тело A вдоль неё
    public Vector3 Point;        // мировая точка контакта
    public float Penetration;    // глубина проникновения (>= 0)
}

/// <summary>
/// Кастомный физический движок. Никаких NuGet-библиотек физики — только своя математика:
/// гравитация, Sphere-vs-AABB, AABB-vs-AABB (SAT по 15 осям), куб-vs-плоскость,
/// последовательные импульсы с учётом вращения, конус трения Кулона, позиционная коррекция.
/// </summary>
public class PhysicsEngine
{
    // ---------------- Настройки мира ----------------
    public const float Gravity = -20f;           // м/с^2 (аркадная сила тяжести)
    public const float FloorY = 0f;              // высота плоскости пола
    public const float LinearDamping = 0.15f;    // сопротивление воздуха
    public const float AngularDamping = 1.2f;    // затухание вращения
    public const int VelocityIterations = 8;     // итераций разрешения скоростей за шаг
    public const float MaxLinearSpeed = 60f;     // клампинг скорости (стабильность)
    public const float MaxAngularSpeed = 30f;

    public const float BallRadius = 0.32f;
    public const float BallMass = 4f;            // тяжёлое "ядро", чтобы пробивало стену
    public const float BallLaunchSpeed = 34f;    // начальная скорость выстрела

    private readonly List<PhysicsObject> _objects = new();
    private readonly List<Contact> _contacts = new(512);

    /// <summary>Вызывается при разрушении кирпича (кирпич, ударившее его тело).</summary>
    public event Action<PhysicsObject, PhysicsObject>? BrickDestroyed;

    public IReadOnlyList<PhysicsObject> Objects => _objects;

    public void Add(PhysicsObject obj) => _objects.Add(obj);

    /// <summary>Удаляет тело из симуляции (например, шар покинул сцену).</summary>
    public bool Remove(PhysicsObject obj) => _objects.Remove(obj);

    public void Clear()
    {
        _objects.Clear();
        _contacts.Clear();
    }

    // =====================================================================
    // Один шаг симуляции (фиксированный dt из Game)
    // =====================================================================
    public void Step(float dt)
    {
        if (dt <= 0f) return;
        dt = MathF.Min(dt, 1f / 30f); // защита от "спирали смерти" при лагах

        // --- 1. Гравитация и демпфирование (полуявный Эйлер: сначала скорость) ---
        foreach (var o in _objects)
        {
            if (!IsSimulated(o)) continue;
            o.Velocity.Y += Gravity * dt;
            o.ApplyDamping(dt, LinearDamping, AngularDamping);
            ClampVelocity(o);
        }

        // --- 2. Сбор контактов (narrow phase) ---
        _contacts.Clear();
        CollectContacts();

        // --- 3. Разрешение скоростей импульсами (sequential impulses) ---
        for (int iter = 0; iter < VelocityIterations; iter++)
        {
            for (int i = 0; i < _contacts.Count; i++)
                ResolveVelocity(_contacts[i]);
        }

        // --- 4. Интегрирование позиции/ориентации ---
        foreach (var o in _objects)
        {
            if (!IsSimulated(o)) continue;
            o.Integrate(dt);
        }

        // --- 5. Позиционная коррекция (убирает проваливание сквозь тела) ---
        for (int i = 0; i < _contacts.Count; i++)
            ResolvePosition(_contacts[i]);

        // --- 6. Покой -> сон ---
        UpdateSleep();
    }

    /// <summary>Кирпичи, ещё "приклеенные" к стене, не интегрируются, но участвуют в контактах как статичные.</summary>
    private static bool IsSimulated(PhysicsObject o) => !o.IsStatic && !o.IsGluedToWall;

    private static void ClampVelocity(PhysicsObject o)
    {
        float len = o.Velocity.Length;
        if (len > MaxLinearSpeed) o.Velocity *= MaxLinearSpeed / len;
        float w = o.AngularVelocity.Length;
        if (w > MaxAngularSpeed) o.AngularVelocity *= MaxAngularSpeed / w;
    }

    // =====================================================================
    // Broad phase (AABB) + Narrow phase
    // =====================================================================
    private void CollectContacts()
    {
        int n = _objects.Count;
        for (int i = 0; i < n; i++)
        {
            var a = _objects[i];

            // Контакт с полом для всех динамических (не приклеенных) тел
            if (!a.IsStatic && !a.IsGluedToWall)
                TryFloorContact(a);

            for (int j = i + 1; j < n; j++)
            {
                var b = _objects[j];
                if (a.IsStaticOrGlued && b.IsStaticOrGlued) continue; // оба "вбиты в бетон"
                if (!AabbOverlap(a, b)) continue;                      // быстрый отсев

                if (a.Shape == ShapeType.Sphere && b.Shape == ShapeType.Box)
                    TrySphereBox(a, b);
                else if (b.Shape == ShapeType.Sphere && a.Shape == ShapeType.Box)
                    TrySphereBox(b, a);
                else if (a.Shape == ShapeType.Sphere && b.Shape == ShapeType.Sphere)
                    TrySphereSphere(a, b);
                else if (a.Shape == ShapeType.Box && b.Shape == ShapeType.Box)
                    TryBoxBox(a, b);
            }
        }
    }

    private static bool AabbOverlap(PhysicsObject a, PhysicsObject b)
    {
        Vector3 amin = a.AabbMin(), amax = a.AabbMax();
        Vector3 bmin = b.AabbMin(), bmax = b.AabbMax();
        return amin.X <= bmax.X && amax.X >= bmin.X
            && amin.Y <= bmax.Y && amax.Y >= bmin.Y
            && amin.Z <= bmax.Z && amax.Z >= bmin.Z;
    }

    // ---------------------------------------------------------------------
    // Сфера против AABB куба. closest-point + clamp по осям — вся математика вручную.
    // ---------------------------------------------------------------------
    private void TrySphereBox(PhysicsObject sphere, PhysicsObject box)
    {
        Vector3 he = box.WorldAabbHalfExtents();
        Vector3 rel = sphere.Position - box.Position;

        // Ближайшая точка AABB к центру сферы
        Vector3 closest = new(
            MathHelper.Clamp(rel.X, -he.X, he.X),
            MathHelper.Clamp(rel.Y, -he.Y, he.Y),
            MathHelper.Clamp(rel.Z, -he.Z, he.Z));

        Vector3 d = rel - closest;
        float distSq = d.LengthSquared;
        float r = sphere.Radius;
        if (distSq > r * r) return;

        Vector3 normalOut; // из куба наружу, к сфере
        float pen;
        if (distSq > 1e-8f)
        {
            float dist = MathF.Sqrt(distSq);
            normalOut = d / dist;
            pen = r - dist;
        }
        else
        {
            // Центр сферы внутри куба — выходим по оси минимального проникновения
            float dx = he.X - MathF.Abs(rel.X);
            float dy = he.Y - MathF.Abs(rel.Y);
            float dz = he.Z - MathF.Abs(rel.Z);
            if (dx <= dy && dx <= dz) { normalOut = new Vector3(MathF.Sign(rel.X), 0, 0); pen = dx + r; }
            else if (dy <= dz)        { normalOut = new Vector3(0, MathF.Sign(rel.Y), 0); pen = dy + r; }
            else                      { normalOut = new Vector3(0, 0, MathF.Sign(rel.Z)); pen = dz + r; }
            if (normalOut.LengthSquared < 0.5f) normalOut = Vector3.UnitX;
        }

        Vector3 point = sphere.Position - normalOut * r;

        // Нормаль "толкает" сферу вдоль normalOut
        AddContact(sphere, box, normalOut, pen, point);

        // Кирпич стены разрушается при любом касании шара
        if (box.GridIndex >= 0 && box.IsGluedToWall)
            DestroyBrick(box, sphere);
    }

    private void TrySphereSphere(PhysicsObject a, PhysicsObject b)
    {
        Vector3 d = b.Position - a.Position;
        float sumR = a.Radius + b.Radius;
        float distSq = d.LengthSquared;
        if (distSq > sumR * sumR || distSq < 1e-9f) return;

        float dist = MathF.Sqrt(distSq);
        Vector3 n = d / dist;
        Vector3 point = a.Position + n * a.Radius;
        // Толкаем A вдоль -n, B вдоль +n: регистрируем контакт для A (норма -n)
        AddContact(a, b, -n, sumR - dist, point);
    }

    // ---------------------------------------------------------------------
    // Куб против куба: SAT по 15 осям (3 оси A, 3 оси B, 9 скрещенных Ai x Bj).
    // ---------------------------------------------------------------------
    private void TryBoxBox(PhysicsObject a, PhysicsObject b)
    {
        Matrix4 ma = Matrix4.CreateFromQuaternion(a.Orientation);
        Matrix4 mb = Matrix4.CreateFromQuaternion(b.Orientation);
        Matrix3 ra = new(ma.Row0.Xyz, ma.Row1.Xyz, ma.Row2.Xyz);
        Matrix3 rb = new(mb.Row0.Xyz, mb.Row1.Xyz, mb.Row2.Xyz);

        Vector3 ha = a.HalfExtents, hb = b.HalfExtents;
        Vector3 t = b.Position - a.Position;

        // R = ra^T * rb (ориентация B в системе координат A)
        // rab[i,j] = dot(Ai, Bj) — ориентация B в системе A (классическая матрица SAT)
        Matrix3 rab = new(
            Vector3.Dot(Axis(ra, 0), Axis(rb, 0)), Vector3.Dot(Axis(ra, 0), Axis(rb, 1)), Vector3.Dot(Axis(ra, 0), Axis(rb, 2)),
            Vector3.Dot(Axis(ra, 1), Axis(rb, 0)), Vector3.Dot(Axis(ra, 1), Axis(rb, 1)), Vector3.Dot(Axis(ra, 1), Axis(rb, 2)),
            Vector3.Dot(Axis(ra, 2), Axis(rb, 0)), Vector3.Dot(Axis(ra, 2), Axis(rb, 1)), Vector3.Dot(Axis(ra, 2), Axis(rb, 2)));

        Span<float> hA = stackalloc float[3] { ha.X, ha.Y, ha.Z };
        Span<float> hB = stackalloc float[3] { hb.X, hb.Y, hb.Z };

        Vector3 tA = new(Vector3.Dot(t, Axis(ra, 0)), Vector3.Dot(t, Axis(ra, 1)), Vector3.Dot(t, Axis(ra, 2)));
        Vector3 tB = new(Vector3.Dot(t, Axis(rb, 0)), Vector3.Dot(t, Axis(rb, 1)), Vector3.Dot(t, Axis(rb, 2)));

        const float EPS = 1e-5f;
        float bestPen = float.MaxValue;
        Vector3 bestNormalWorld = default; // нормаль от A к B
        bool found = false;

        // ---- 3 оси A ----
        for (int i = 0; i < 3; i++)
        {
            float rA = hA[i];
            float rB = hB[0] * MathF.Abs(rab[0, i]) + hB[1] * MathF.Abs(rab[1, i]) + hB[2] * MathF.Abs(rab[2, i]);
            float dist = MathF.Abs(tA[i]);
            float pen = rA + rB - dist;
            if (pen < 0f) return; // разделяющая ось — пересечения нет
            if (pen < bestPen)
            {
                bestPen = pen; found = true;
                bestNormalWorld = Axis(ra, i) * (tA[i] >= 0 ? 1f : -1f);
            }
        }

        // ---- 3 оси B ----
        for (int j = 0; j < 3; j++)
        {
            float rA = hA[0] * MathF.Abs(rab[j, 0]) + hA[1] * MathF.Abs(rab[j, 1]) + hA[2] * MathF.Abs(rab[j, 2]);
            float rB = hB[j];
            float dist = MathF.Abs(tB[j]);
            float pen = rA + rB - dist;
            if (pen < 0f) return;
            if (pen < bestPen)
            {
                bestPen = pen; found = true;
                bestNormalWorld = Axis(rb, j) * (tB[j] >= 0 ? 1f : -1f);
            }
        }

        // ---- 9 скрещенных осей L = Ai x Bj ----
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 axis = Vector3.Cross(Axis(ra, i), Axis(rb, j));
                float lenSq = axis.LengthSquared;
                if (lenSq < 1e-8f) continue; // почти параллельные рёбра -> вырожденная ось
                axis /= MathF.Sqrt(lenSq);

                int i1 = (i + 1) % 3, i2 = (i + 2) % 3;
                int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                float rA = hA[i1] * MathF.Abs(rab[j, i1]) + hA[i2] * MathF.Abs(rab[j, i2]);
                float rB = hB[j1] * MathF.Abs(rab[j1, i]) + hB[j2] * MathF.Abs(rab[j2, i]);
                float dist = MathF.Abs(Vector3.Dot(t, axis));
                float pen = rA + rB - dist + EPS;
                if (pen < 0f) return;
                if (pen < bestPen)
                {
                    bestPen = pen; found = true;
                    float s = Vector3.Dot(t, axis);
                    bestNormalWorld = axis * (s >= 0 ? 1f : -1f);
                }
            }
        }

        if (!found) return;

        Vector3 normal = Vector3.Normalize(bestNormalWorld); // от A к B
        Vector3 point = (a.Position + b.Position) * 0.5f;

        // Контакт толкает A вдоль -normal (зарегистрировать), B — вдоль +normal
        AddContact(a, b, -normal, bestPen, point);

        // Каскадное разрушение: быстрое свободное тело выбивает приклеенный кирпич
        if (b.GridIndex >= 0 && b.IsGluedToWall && RelativeSpeed(a, b) > 5f)
            DestroyBrick(b, a);
        if (a.GridIndex >= 0 && a.IsGluedToWall && RelativeSpeed(b, a) > 5f)
            DestroyBrick(a, b);
    }


    /// <summary>Ось тела i (0=X,1=Y,2=Z) в мировых координатах. В OpenTK матрица хранится построчно,
    /// поэтому базисные векторы лежат в Row0..Row2.</summary>
    private static Vector3 Axis(Matrix3 m, int i) => i switch { 0 => m.Row0, 1 => m.Row1, _ => m.Row2 };

    private static float RelativeSpeed(PhysicsObject hitter, PhysicsObject victim)
        => (hitter.Velocity - victim.Velocity).Length;

    // ---------------------------------------------------------------------
    // Тело против плоскости пола (нормаль +Y)
    // ---------------------------------------------------------------------
    private void TryFloorContact(PhysicsObject body)
    {
        Vector3 he = body.WorldAabbHalfExtents();
        float bottom = body.Position.Y - he.Y;
        if (bottom >= FloorY) return;

        float pen = FloorY - bottom;
        Vector3 point = new(body.Position.X, FloorY, body.Position.Z);
        AddContact(body, null!, Vector3.UnitY, pen, point);
    }

    // =====================================================================
    // Контакты и импульсы
    // =====================================================================
    private void AddContact(PhysicsObject pushTarget, PhysicsObject other, Vector3 pushNormal, float pen, Vector3 point)
    {
        _contacts.Add(new Contact
        {
            A = pushTarget,
            B = other,          // может быть null (пол)
            Normal = pushNormal,
            Penetration = pen,
            Point = point,
        });
        pushTarget.Wake();
        other?.Wake();
    }

    /// <summary>Эффективная масса точки контакта с учётом вращения (r x n).</summary>
    private static float EffectiveMass(PhysicsObject o, Vector3 r, Vector3 n)
    {
        if (o.IsStaticOrGlued) return 0f;
        Vector3 rxn = Vector3.Cross(r, n);
        return o.InvMass + o.InvInertia * Vector3.Dot(rxn, rxn);
    }

    private void ResolveVelocity(Contact c)
    {
        var a = c.A;
        var b = c.B;
        Vector3 n = c.Normal;

        Vector3 rA = c.Point - a.Position;
        Vector3 rB = b is null ? Vector3.Zero : c.Point - b.Position;

        // Скорость точки тела: v + ω × r
        Vector3 velA = a.Velocity + Vector3.Cross(a.AngularVelocity, rA);
        Vector3 velB = b is null ? Vector3.Zero : b.Velocity + Vector3.Cross(b.AngularVelocity, rB);
        Vector3 rv = velB - velA;

        float vn = Vector3.Dot(rv, n);
        if (vn > 0f) return; // тела уже расходятся

        float e = MathF.Max(a.Restitution, b is null ? 0.25f : b.Restitution);
        // Порог покоя: без него объекты "дрожат" от бесконечных микро-отскоков
        if (-vn < 1.0f) e = 0f;

        float k = EffectiveMass(a, rA, n) + (b is null ? 0f : EffectiveMass(b, rB, n));
        if (k < 1e-7f) return;

        float jn = -(1f + e) * vn / k;
        ApplyImpulse(a, rA, n * jn);
        if (b is not null) ApplyImpulse(b, rB, n * -jn);

        // --- Трение (касательная плоскости контакта, конус Кулона) ---
        velA = a.Velocity + Vector3.Cross(a.AngularVelocity, rA);
        velB = b is null ? Vector3.Zero : b.Velocity + Vector3.Cross(b.AngularVelocity, rB);
        rv = velB - velA;

        Vector3 tangent = rv - n * Vector3.Dot(rv, n);
        float tLen = tangent.Length;
        if (tLen < 1e-6f) return;
        tangent /= tLen;

        float kt = EffectiveMass(a, rA, tangent) + (b is null ? 0f : EffectiveMass(b, rB, tangent));
        if (kt < 1e-7f) return;

        float jt = -Vector3.Dot(rv, tangent) / kt;
        float mu = MathF.Sqrt(a.Friction * (b is null ? 0.7f : b.Friction));
        jt = MathHelper.Clamp(jt, -mu * jn, mu * jn);

        ApplyImpulse(a, rA, tangent * jt);
        if (b is not null) ApplyImpulse(b, rB, tangent * -jt);
    }

    private static void ApplyImpulse(PhysicsObject o, Vector3 r, Vector3 impulse)
    {
        if (o.IsStaticOrGlued) return;
        o.Velocity += impulse * o.InvMass;
        o.AngularVelocity += Vector3.Cross(r, impulse) * o.InvInertia;
    }

    /// <summary>Прямая позиционная коррекция — убирает "проваливание" тел друг в друга.</summary>
    private void ResolvePosition(Contact c)
    {
        var a = c.A;
        var b = c.B;
        float invA = a.IsStaticOrGlued ? 0f : a.InvMass;
        float invB = b is null || b.IsStaticOrGlued ? 0f : b.InvMass;
        float sum = invA + invB;
        if (sum <= 0f) return;

        const float percent = 0.5f;
        const float slop = 0.005f;
        float corr = MathF.Max(c.Penetration - slop, 0f) / sum * percent;
        Vector3 n = c.Normal;

        a.Position += n * (corr * invA);
        if (b is not null) b.Position -= n * (corr * invB);
    }

    private void UpdateSleep()
    {
        foreach (var o in _objects)
        {
            if (!IsSimulated(o) || o.Sleeping) continue;

            bool slow = o.Velocity.LengthSquared < 0.02f && o.AngularVelocity.LengthSquared < 0.05f;
            bool onGround = o.Position.Y - o.WorldAabbHalfExtents().Y <= FloorY + 0.08f;
            if (slow && onGround)
            {
                o.Velocity = Vector3.Zero;
                o.AngularVelocity = Vector3.Zero;
                o.Sleeping = true;
            }
        }
    }

    // =====================================================================
    // Разрушение стены
    // =====================================================================
    private void DestroyBrick(PhysicsObject brick, PhysicsObject hitter)
    {
        if (!brick.IsGluedToWall) return;
        brick.IsGluedToWall = false;
        brick.Wake();

        // Вектор удара: направление полёта атакующего, либо линия от его центра к кирпичу
        Vector3 dir = hitter.Velocity;
        if (dir.LengthSquared < 0.25f)
            dir = brick.Position - hitter.Position;
        dir = Vector3.Normalize(dir);

        float power = MathF.Max(hitter.Mass * hitter.Velocity.Length * 0.6f, 6f);

        Random rng = Random.Shared;
        // Импульс: по направлению удара + разброс в стороны + немного вверх
        Vector3 kick = dir * power
            + new Vector3(
                (float)(rng.NextDouble() - 0.5) * power * 0.5f,
                (float)rng.NextDouble() * power * 0.25f,
                (float)(rng.NextDouble() - 0.5) * power * 0.5f);

        brick.ApplyImpulse(kick);
        brick.ApplyAngularImpulse(new Vector3(
            (float)(rng.NextDouble() - 0.5) * 6f,
            (float)(rng.NextDouble() - 0.5) * 6f,
            (float)(rng.NextDouble() - 0.5) * 6f));

        BrickDestroyed?.Invoke(brick, hitter);
    }
}
