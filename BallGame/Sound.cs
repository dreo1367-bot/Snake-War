namespace BallGame;

/// <summary>Простой звук через Console.Beep в фоновом потоке, чтобы не тормозить игру.</summary>
public static class Sound
{
    public static bool Enabled = true;

    public static void Bounce(float impact)
    {
        if (!Enabled) return;
        int freq = (int)Math.Clamp(180 + impact * 0.25f, 120, 1400);
        Play(freq, 30);
    }

    public static void Throw(float power)
    {
        if (!Enabled) return;
        Play((int)Math.Clamp(500 + power * 0.3f, 300, 1800), 60);
        Play((int)Math.Clamp(900 + power * 0.4f, 500, 2200), 50);
    }

    private static void Play(int freq, int dur) =>
        Task.Run(() => { try { Console.Beep(freq, dur); } catch { } });
}
