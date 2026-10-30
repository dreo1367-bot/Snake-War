using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace BrickBreaker3D;

/// <summary>
/// Загрузка, компиляция и линковка GLSL-шейдеров (OpenGL 3.3+),
/// а также кэш uniform-locаций.
/// </summary>
public class Shader : IDisposable
{
    public int Program { get; private set; }
    private bool _disposed;

    public Shader(string vertexPath, string fragmentPath)
    {
        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);
        Compile(vertexSource, fragmentSource);
    }

    public Shader(ShaderSource source)
    {
        Compile(source.Vertex, source.Fragment);
    }

    private void Compile(string vertexSource, string fragmentSource)
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);
        CheckShader(vertexShader, "vertex");

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);
        CheckShader(fragmentShader, "fragment");

        Program = GL.CreateProgram();
        GL.AttachShader(Program, vertexShader);
        GL.AttachShader(Program, fragmentShader);
        GL.LinkProgram(Program);

        GL.GetProgram(Program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Link error: {GL.GetProgramInfoLog(Program)}");

        // Логи шейдеров больше не нужны — отвязываем и удаляем
        GL.DetachShader(Program, vertexShader);
        GL.DetachShader(Program, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    private static void CheckShader(int shader, string label)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
            throw new Exception($"{label} shader compile error:\n{GL.GetShaderInfoLog(shader)}\n---source---");
    }

    public void Use() => GL.UseProgram(Program);

    private readonly Dictionary<string, int> _uniformCache = new();

    private int GetUniform(string name)
    {
        if (!_uniformCache.TryGetValue(name, out int loc))
        {
            loc = GL.GetUniformLocation(Program, name);
            _uniformCache[name] = loc;
        }
        return loc;
    }

    public void SetMatrix4(string name, Matrix4 m) => GL.UniformMatrix4(GetUniform(name), false, ref m);
    public void SetVector3(string name, Vector3 v) => GL.Uniform3(GetUniform(name), v);
    public void SetFloat(string name, float v) => GL.Uniform1(GetUniform(name), v);
    public void SetInt(string name, int v) => GL.Uniform1(GetUniform(name), v);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteProgram(Program);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Встроенные исходники шейдеров (используются, если файловая папка shaders/ не найдена).</summary>
public struct ShaderSource
{
    public required string Vertex;
    public required string Fragment;
}
