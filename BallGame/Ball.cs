namespace BallGame;

/// <summary>Физический мячик: гравитация, отскоки, трение, вращение.</summary>
public class Ball
{
    public float X, Y;          // центр
    public float R;             // радиус
    public float VX, VY;        // скорость px/s
    public float Angle;         // угол вращения
    public float Spin;          // угловая скорость град/с
    public Color Color;
    public string Face;         // смайлик настроения

    private const float Gravity = 2600f;
    private const float Restitution = 0.78f;   // упругость пола
    private const float WallRestitution = 0.85f;
    private const float AirDrag = 0.12f;       // сопротивление воздуха
    private const float FloorFriction = 1.6f;

    public Ball(float x, float y, float r, Color color, string face)
    {
        X = x; Y = y; R = r; Color = color; Face = face;
    }

    public void Update(float dt, int w, int h)
    {
        VY += Gravity * dt;
        VX -= VX * AirDrag * dt;
        X += VX * dt;
        Y += VY * dt;
        Angle += Spin * dt;

        // Пол
        if (Y + R > h)
        {
            Y = h - R;
            if (VY > 40) VY = -VY * Restitution; else VY = 0;
            VX -= VX * FloorFriction * dt * 3;
            Spin = -VX / R * 57.3f * 0.6f + Spin * 0.3f;
        }
        // Потолок
        if (Y - R < 0) { Y = R; VY = -VY * WallRestitution; }
        // Стены
        if (X - R < 0) { X = R; VX = -VX * WallRestitution; Spin = -Spin * 0.5f; }
        if (X + R > w) { X = w - R; VX = -VX * WallRestitution; Spin = -Spin * 0.5f; }

        // Мяч "злятся" если быстро летит и ударяется о стену
        var speed = MathF.Sqrt(VX * VX + VY * VY);
        Face = speed > 900 ? "😵" : speed > 400 ? "😄" : "🙂";
    }

    public bool Contains(float px, float py)
    {
        float dx = px - X, dy = py - Y;
        return dx * dx + dy * dy <= (R + 6) * (R + 6);
    }

    public void Draw(Graphics g)
    {
        var state = g.Save();
        g.TranslateTransform(X, Y);
        g.RotateTransform(Angle);

        using var br = new SolidBrush(Color);
        using var pen = new Pen(Color.Darken(40), 2);
        g.FillEllipse(br, -R, -R, 2 * R, 2 * R);
        g.DrawEllipse(pen, -R, -R, 2 * R, 2 * R);

        // полоски чтобы было видно вращение
        using var stripe = new SolidBrush(Color.Darken(25));
        g.FillPie(stripe, -R, -R, 2 * R, 2 * R, 20, 40);
        g.FillPie(stripe, -R, -R, 2 * R, 2 * R, 200, 40);

        // глазик
        g.ResetTransform();
        g.TranslateTransform(X, Y);
        using var font = new Font("Segoe UI Emoji", R * 0.9f, GraphicsUnit.Pixel);
        var sz = g.MeasureString(Face, font);
        g.DrawString(Face, font, Brushes.Black, -sz.Width / 2, -sz.Height / 2);
        g.Restore(state);
    }

    public static Color RandColor(Random rnd)
    {
        var palette = new[]
        {
            Color.FromArgb(255, 99, 132), Color.FromArgb(54, 162, 235),
            Color.FromArgb(255, 205, 86), Color.FromArgb(75, 192, 192),
            Color.FromArgb(153, 102, 255), Color.FromArgb(255, 159, 64),
            Color.FromArgb(46, 204, 113), Color.FromArgb(231, 76, 60)
        };
        return palette[rnd.Next(palette.Length)];
    }
}

public static class ColorExtensions
{
    public static Color Darken(this Color c, int by) =>
        Color.FromArgb(c.A, Math.Max(0, c.R - by), Math.Max(0, c.G - by), Math.Max(0, c.B - by));
}
