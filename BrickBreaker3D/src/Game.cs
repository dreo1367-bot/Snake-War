using BrickBreaker3D.Physics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BrickBreaker3D;

/// <summary>
/// Главный игровой цикл: сцена (стена + пол), управление камерой, стрельба шарами (F),
/// фиксированный шаг кастомной физики и рендер через один шейдер с диффузным светом.
/// </summary>
public class Game : GameWindow
{
    // ---------------- Параметры стены ----------------
    private const int WallCols = 6;        // кирпичей по горизонтали
    private const int WallRows = 6;        // кирпичей по вертикали
    private const float BrickSize = 1.5f;  // сторона кирпича (м)
    private const float BrickDepth = 0.9f; // толщина кирпича
    private const float WallGap = 0.02f;   // микрозазор между кирпичами (SAT не "залипает")
    private const float WallZ = 8f;        // стена стоит на +Z, игрок смотрит из -Z
    private const float WallCenterY = 5f;  // центр стены над полом

    private const float FixedDt = 1f / 120f; // физика на 120 Гц — стабильные контакты
    private const int MaxStepsPerFrame = 8;
    private const int MaxBalls = 12;         // пул шаров: старые вытесняются новыми

    private readonly Camera _camera = new();
    private readonly PhysicsEngine _physics = new();
    private Shader _shader = null!;
    private Mesh _cubeMesh = null!;
    private Mesh _sphereMesh = null!;
    private Mesh _floorMesh = null!;

    private readonly List<PhysicsObject> _bricks = new(WallCols * WallRows);
    private readonly List<PhysicsObject> _balls = new();

    private double _accumulator;
    private Vector2 _lastMousePos;
    private bool _hasLastMouse;
    private int _destroyedCount;
    private int _shotsFired;
    private readonly Vector3 _lightPos = new(-6f, 14f, -8f);
    private readonly Random _rng = new(1337);

    public Game() : base(
        GameWindowSettings.Default,
        new NativeWindowSettings
        {
            Title = "Brick Breaker 3D — Custom Physics (OpenTK)",
            ClientSize = new Vector2i(1280, 720),
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
        })
    {
    }

    // =====================================================================
    // Инициализация
    // =====================================================================
    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(new Color4(0.10f, 0.11f, 0.14f, 1f));
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);

        _shader = CreateShaderWithFallback();

        _cubeMesh = Mesh.CreateCube();
        _sphereMesh = Mesh.CreateSphere(20, 28);
        _floorMesh = Mesh.CreateFloorQuad(60f);

        BuildScene();
        _physics.BrickDestroyed += OnBrickDestroyed;

        CursorState = CursorState.Grabbed;
        _camera.AspectRatio = (float)Size.X / Math.Max(1, Size.Y);
    }

    /// <summary>Сначала пробуем внешние файлы shaders/, при неудаче — встроенные исходники.</summary>
    private static Shader CreateShaderWithFallback()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "shaders");
        string vert = Path.Combine(dir, "model.vert");
        string frag = Path.Combine(dir, "model.frag");
        if (File.Exists(vert) && File.Exists(frag))
        {
            try { return new Shader(vert, frag); }
            catch (Exception e) { Console.WriteLine($"[warn] external shaders failed: {e.Message}"); }
        }
        return new Shader(new ShaderSource
        {
            Vertex = EmbeddedShaders.Vertex,
            Fragment = EmbeddedShaders.Fragment,
        });
    }

    private void BuildScene()
    {
        _bricks.Clear();
        _balls.Clear();
        _physics.Clear();
        _destroyedCount = 0;
        _shotsFired = 0;
        _accumulator = 0;

        float halfW = (WallCols - 1) * (BrickSize + WallGap) * 0.5f;
        float halfH = (WallRows - 1) * (BrickSize + WallGap) * 0.5f;

        for (int row = 0; row < WallRows; row++)
        {
            for (int col = 0; col < WallCols; col++)
            {
                var pos = new Vector3(
                    -halfW + col * (BrickSize + WallGap),
                    WallCenterY - halfH + row * (BrickSize + WallGap),
                    WallZ);

                const float mass = 2.2f;
                var size = new Vector3(BrickSize, BrickSize, BrickDepth);

                var brick = new PhysicsObject
                {
                    Position = pos,
                    Shape = ShapeType.Box,
                    HalfExtents = size * 0.5f,
                    Mass = mass,
                    Inertia = ComputeBoxInertia(mass, size),
                    Restitution = 0.18f,
                    Friction = 0.6f,
                    GridIndex = row * WallCols + col,
                    IsGluedToWall = true,
                    MeshKey = "cube",
                    // Сплошной цвет (текстур нет). Лёгкая вариация серого — это material color, не текстура.
                    Color = new Vector3(
                        0.86f + (float)_rng.NextDouble() * 0.10f,
                        0.86f + (float)_rng.NextDouble() * 0.10f,
                        0.88f + (float)_rng.NextDouble() * 0.10f),
                };

                _bricks.Add(brick);
                _physics.Add(brick);
            }
        }

        _camera.ResetPose();
    }

    /// <summary>I = m/12 * (w² + h² + d²) — момент инерции куба (приближённо изотропный).</summary>
    private static float ComputeBoxInertia(float mass, Vector3 size)
    {
        return mass / 12f * (size.X * size.X + size.Y * size.Y + size.Z * size.Z);
    }

    // =====================================================================
    // Ввод + обновление
    // =====================================================================
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float dt = (float)Math.Clamp(args.Time, 0.0, 0.1);
        KeyboardState kb = KeyboardState;

        if (kb.IsKeyDown(Keys.Escape))
            Close();

        // --- Движение камеры (WASD + Q/E, Shift — ускорение) ---
        _camera.OnUpdate(dt, kb);

        // --- Вращение мышью ---
        MouseState ms = MouseState;
        Vector2 mousePos = ms.Position;
        if (_hasLastMouse)
        {
            Vector2 delta = mousePos - _lastMousePos;
            if (delta.LengthSquared < 100_000f) // защита от "телепорта" курсора при потере фокуса
                _camera.OnMouseMove(delta);
        }
        _lastMousePos = mousePos;
        _hasLastMouse = true;

        // --- Выстрел по F ---
        if (kb.IsKeyPressed(Keys.F))
            FireBall();

        // --- R — перестроить стену заново ---
        if (kb.IsKeyPressed(Keys.R))
            BuildScene();

        // --- Фиксированный шаг физики ---
        _accumulator += dt;
        int steps = 0;
        while (_accumulator >= FixedDt && steps < MaxStepsPerFrame)
        {
            _physics.Step(FixedDt);
            _accumulator -= FixedDt;
            steps++;
        }
        if (steps == MaxStepsPerFrame)
            _accumulator = 0; // если сильно отстали — не догоняем бесконечно

        CleanupBalls();

        Title = $"Brick Breaker 3D | shots: {_shotsFired} | bricks destroyed: {_destroyedCount}/{_bricks.Count} | bodies: {_physics.Objects.Count}";
    }

    /// <summary>Спавн шара из камеры в направлении взгляда.</summary>
    private void FireBall()
    {
        Vector3 dir = _camera.Front;
        if (dir.LengthSquared < 1e-6f) dir = Vector3.UnitZ;
        dir = Vector3.Normalize(dir);

        var ball = new PhysicsObject
        {
            Position = _camera.Position + dir * 0.7f,
            Velocity = dir * PhysicsEngine.BallLaunchSpeed,
            Shape = ShapeType.Sphere,
            HalfExtents = Vector3.One * PhysicsEngine.BallRadius,
            Mass = PhysicsEngine.BallMass,
            Inertia = 0.4f,
            Restitution = 0.55f,
            Friction = 0.3f,
            GridIndex = -1,
            IsGluedToWall = false,
            MeshKey = "sphere",
            Color = new Vector3(0.95f, 0.95f, 0.98f),
        };

        _balls.Add(ball);
        _physics.Add(ball);
        _shotsFired++;

        // Ограничиваем количество живых шаров: самый старый покидает сцену
        while (_balls.Count > MaxBalls)
        {
            var old = _balls[0];
            _balls.RemoveAt(0);
            _physics.Remove(old);
        }
    }

    /// <summary>Убираем шары, улетевшие далеко или долго лежащие в покое.</summary>
    private void CleanupBalls()
    {
        for (int i = _balls.Count - 1; i >= 0; i--)
        {
            var b = _balls[i];
            bool far = b.Position.Length > 150f || b.Position.Y < -20f;
            if (far)
            {
                _balls.RemoveAt(i);
                _physics.Remove(b);
            }
        }
    }

    // =====================================================================
    // Рендер
    // =====================================================================
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();
        _shader.SetMatrix4("uView", _camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", _camera.GetProjectionMatrix());
        _shader.SetVector3("uViewPos", _camera.Position);
        _shader.SetVector3("uLightPos", _lightPos);

        // Пол (плоскость Y=0)
        _shader.SetVector3("uObjectColor", new Vector3(0.70f, 0.70f, 0.74f));
        _shader.SetMatrix4("uModel", Matrix4.CreateTranslation(0f, PhysicsEngine.FloorY, 0f));
        _floorMesh.Draw();

        // Все физические тела: кирпичи (кубы) и шары (сферы)
        foreach (var o in _physics.Objects)
        {
            _shader.SetVector3("uObjectColor", o.Color);
            _shader.SetMatrix4("uModel", o.GetModelMatrix());
            (o.Shape == ShapeType.Sphere ? _sphereMesh : _cubeMesh).Draw();
        }

        DrawCrosshair();

        SwapBuffers();
    }

    /// <summary>Прицел — маленький красный крест из кубиков перед камерой (без текстур).</summary>
    private void DrawCrosshair()
    {
        const float dist = 2.2f;
        Vector3 center = _camera.Position + _camera.Front * dist;
        Vector3 right = _camera.Right;
        Vector3 up = _camera.Up;

        float s = 0.02f; // толщина лучины
        float l = 0.10f; // длина лучины

        _shader.SetVector3("uObjectColor", new Vector3(1f, 0.3f, 0.3f));

        Vector3[] offsets =
        {
            right * l * 0.75f, -right * l * 0.75f, up * l * 0.75f, -up * l * 0.75f,
        };
        Vector3[] scales =
        {
            new Vector3(l, s, s), new Vector3(l, s, s),
            new Vector3(s, l, s), new Vector3(s, l, s),
        };

        for (int i = 0; i < 4; i++)
        {
            Matrix4 model = Matrix4.CreateScale(scales[i])
                          * Matrix4.CreateTranslation(center + offsets[i]);
            _shader.SetMatrix4("uModel", model);
            _cubeMesh.Draw();
        }
    }

    private void OnBrickDestroyed(PhysicsObject brick, PhysicsObject hitter)
    {
        _destroyedCount++;
    }

    // =====================================================================
    // Прочее
    // =====================================================================
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Math.Max(1, e.Width), Math.Max(1, e.Height));
        _camera.AspectRatio = (float)e.Width / Math.Max(1, e.Height);
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        _shader.Dispose();
        _cubeMesh.Dispose();
        _sphereMesh.Dispose();
        _floorMesh.Dispose();
    }
}
