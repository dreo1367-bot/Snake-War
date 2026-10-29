namespace BallGame;

/// <summary>Частицы: искры при ударе, звёздочки при броске.</summary>
public class Particle
{
    public float X, Y, VX, VY, Life, MaxLife, Size;
    public Color Color;

    public void Update(float dt)
    {
        VY += 900 * dt;
        X += VX * dt; Y += VY * dt;
        Life -= dt;
    }

    public void Draw(Graphics g)
    {
        var a = (int)Math.Clamp(255 * Life / MaxLife, 0, 255);
        using var br = new SolidBrush(Color.FromArgb(a, Color.R, Color.G, Color.B));
        g.FillEllipse(br, X - Size / 2, Y - Size / 2, Size, Size);
    }
}

public static class Particles
{
    public static List<Particle> Spawn(Random rnd, float x, float y, Color baseColor, int count, float power)
    {
        var list = new List<Particle>(count);
        for (int i = 0; i < count; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            float sp = (float)(rnd.NextDouble() * power + power * 0.2);
            float life = 0.3f + (float)rnd.NextDouble() * 0.5f;
            list.Add(new Particle
            {
                X = x, Y = y,
                VX = (float)Math.Cos(ang) * sp,
                VY = (float)Math.Sin(ang) * sp - power * 0.4f,
                Life = life, MaxLife = life,
                Size = 3 + (float)rnd.NextDouble() * 6,
                Color = rnd.Next(3) == 0 ? Color.White : baseColor
            });
        }
        return list;
    }
}
