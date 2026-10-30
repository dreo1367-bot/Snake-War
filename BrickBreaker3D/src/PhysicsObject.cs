using OpenTK.Mathematics;

namespace BrickBreaker3D.Physics;

/// <summary>
/// Базовый класс физического тела (PhysicsObject).
/// Хранит массу, скорость, позицию, ориентацию и параметры материалов.
/// Вся физика в проекте — кастомная, сторонние движки не используются.
/// </summary>
public class PhysicsObject
{
    // ---------------- Кинематика ----------------
    public Vector3 Position;                 // центр масс в мировых координатах
    public Quaternion Orientation = Quaternion.Identity;
    public Vector3 Velocity;                 // м/с
    public Vector3 AngularVelocity;          // рад/с, псевдо-вектор (ось * величина)

    // ---------------- Материалы ----------------
    public float Mass = 1f;                  // кг
    public float Inertia = 1f;               // кг*м^2 (приближённо, изотропно)
    public float Restitution = 0.35f;        // упругость (0..1)
    public float Friction = 0.45f;           // коэффициент трения Кулона

    public float InvMass => IsStatic ? 0f : 1f / MathF.Max(Mass, 1e-6f);
    public float InvInertia => IsStatic ? 0f : 1f / MathF.Max(Inertia, 1e-6f);

    // ---------------- Форма ----------------
    public ShapeType Shape = ShapeType.Box;
    /// <summary>Полуразмеры куба (Box) или радиус по осям (Sphere).</summary>
    public Vector3 HalfExtents = new(0.5f, 0.5f, 0.5f);

    public float Radius => Shape == ShapeType.Sphere ? HalfExtents.X : 0f;

    public bool IsStatic;                    // статичные тела (пол) не двигаются

    // ---------------- Состояние "стены" ----------------
    /// <summary>Индекс кирпича в сетке стены (-1 — объект не часть стены).</summary>
    public int GridIndex = -1;
    /// <summary>True — кирпич ещё "склеен" со стеной и не падает (для физики он статичен).</summary>
    public bool IsGluedToWall = true;

    /// <summary>Стена (кирпичи) или пол: тело, которое физика не двигает, пока оно целое.</summary>
    public bool IsStaticOrGlued => IsStatic || IsGluedToWall;
    /// <summary>Ссылка на меш-ассет для рендера (Cube/Sphere).</summary>
    public string MeshKey = "cube";
    /// <summary>Сплошной цвет объекта (без текстур!).</summary>
    public Vector3 Color = new(0.92f, 0.92f, 0.95f);
    /// <summary>Если тело "спит" (покоится), физика его пропускает до нового контакта.</summary>
    public bool Sleeping = false;

    /// <summary>Модельная матрица: масштаб -> поворот -> перенос.</summary>
    public Matrix4 GetModelMatrix()
    {
        return Matrix4.CreateScale(HalfExtents * 2f)
             * Matrix4.CreateFromQuaternion(Orientation)
             * Matrix4.CreateTranslation(Position);
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        if (IsStatic) return;
        Velocity += impulse * InvMass;
        Wake();
    }

    public void ApplyAngularImpulse(Vector3 angularImpulse)
    {
        if (IsStatic) return;
        AngularVelocity += angularImpulse * InvInertia;
        Wake();
    }

    public void Wake()
    {
        Sleeping = false;
    }

    /// <summary>Экспоненциальное затухание (сопротивление воздуха) за шаг dt.</summary>
    public void ApplyDamping(float dt, float linearDamping, float angularDamping)
    {
        if (IsStatic || Sleeping) return;
        Velocity *= MathF.Exp(-linearDamping * dt);
        AngularVelocity *= MathF.Exp(-angularDamping * dt);
    }

    /// <summary>Полуявное (симплектическое) интегрирование Эйлера.</summary>
    public void Integrate(float dt)
    {
        if (IsStatic || Sleeping) return;

        Position += Velocity * dt;

        float w = AngularVelocity.Length;
        if (w > 1e-5f)
        {
            Vector3 axis = AngularVelocity / w;
            float angle = MathHelper.Clamp(w * dt, -0.4f, 0.4f);
            var dq = Quaternion.FromAxisAngle(axis, angle);
            Orientation = Quaternion.Normalize(dq * Orientation);
        }
    }

    /// <summary>
    /// Консервативные мировые полуразмеры AABB повёрнутого куба: |R| * h.
    /// Используется как грубая фаза отсечения (broad phase) и для устойчивости контактов.
    /// </summary>
    public Vector3 WorldAabbHalfExtents()
    {
        Matrix4 mq = Matrix4.CreateFromQuaternion(Orientation);
        Matrix3 m = new(mq.Row0.Xyz, mq.Row1.Xyz, mq.Row2.Xyz);
        Vector3 h = HalfExtents;
        return new Vector3(
            MathF.Abs(m.M11) * h.X + MathF.Abs(m.M12) * h.Y + MathF.Abs(m.M13) * h.Z,
            MathF.Abs(m.M21) * h.X + MathF.Abs(m.M22) * h.Y + MathF.Abs(m.M23) * h.Z,
            MathF.Abs(m.M31) * h.X + MathF.Abs(m.M32) * h.Y + MathF.Abs(m.M33) * h.Z);
    }

    public Vector3 AabbMin() => Position - WorldAabbHalfExtents();
    public Vector3 AabbMax() => Position + WorldAabbHalfExtents();
}

public enum ShapeType
{
    Box,
    Sphere,
}
