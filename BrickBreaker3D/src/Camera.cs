using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BrickBreaker3D;

/// <summary>
/// FPS-камера: LookAt-матрица + перспективная проекция.
/// Мышь вращает (yaw/pitch), WASD (+Q/E) двигает в плоскости взгляда.
/// </summary>
public class Camera
{
    public Vector3 Position = new(0f, 4f, -12f);

    private float _yaw = 0f;      // радиусы, вокруг оси Y
    private float _pitch = 0f;    // радиусы, вверх/вниз

    public float MovementSpeed = 8f;
    public float MouseSensitivity = 0.0025f;
    public float FovDegrees = 60f;
    public float NearPlane = 0.1f;
    public float FarPlane = 300f;
    public float AspectRatio = 16f / 9f;

    /// <summary>Направление взгляда (нормализовано).</summary>
    public Vector3 Front
    {
        get
        {
            // Сферические координаты -> декартовы
            float cp = MathF.Cos(_pitch);
            Vector3 f = new(cp * MathF.Sin(_yaw), MathF.Sin(_pitch), cp * MathF.Cos(_yaw));
            return Vector3.Normalize(f);
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
    public Vector3 Up => Vector3.Cross(Right, Front);

    public Matrix4 GetViewMatrix()
        => Matrix4.LookAt(Position, Position + Front, Vector3.UnitY);

    public Matrix4 GetProjectionMatrix()
        => Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FovDegrees), AspectRatio, NearPlane, FarPlane);

    public void OnMouseMove(Vector2 delta)
    {
        _yaw += delta.X * MouseSensitivity;
        _pitch -= delta.Y * MouseSensitivity;
        const float limit = MathHelper.PiOver2 - 0.01f;
        _pitch = MathHelper.Clamp(_pitch, -limit, limit);
        // Нормализуем yaw в [0, 2pi), чтобы не переполнять float при долгой игре
        _yaw %= MathHelper.TwoPi;
    }

    public void OnUpdate(float dt, KeyboardState kb)
    {
        var input = Vector3.Zero;
        if (kb.IsKeyDown(Keys.W)) input += Front;
        if (kb.IsKeyDown(Keys.S)) input -= Front;
        if (kb.IsKeyDown(Keys.D)) input += Right;
        if (kb.IsKeyDown(Keys.A)) input -= Right;
        if (kb.IsKeyDown(Keys.E)) input += Vector3.UnitY;
        if (kb.IsKeyDown(Keys.Q)) input -= Vector3.UnitY;

        if (input.LengthSquared > 1e-6f)
        {
            input = Vector3.Normalize(input);
            float speed = MovementSpeed;
            if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
                speed *= 2.5f;
            Position += input * (speed * dt);
        }

        // Не даём камере провалиться под пол
        if (Position.Y < 0.4f) Position.Y = 0.4f;
    }

    public void ResetPose()
    {
        Position = new Vector3(0f, 4f, -12f);
        _yaw = 0f;
        _pitch = 0f;
    }
}
