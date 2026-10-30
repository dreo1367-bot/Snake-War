using OpenTK.Windowing.Desktop;

namespace BrickBreaker3D;

/// <summary>Точка входа: создаёт окно OpenTK и запускает игровой цикл.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new Game();
        game.Run();
    }
}
