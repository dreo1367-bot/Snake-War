using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace BrickBreaker3D;

/// <summary>
/// Геометрия примитивов (Куб и Сфера) с интерлеавингом pos+normal.
/// Никаких текстур — только позиции и нормали для освещения.
/// </summary>
public class Mesh : IDisposable
{
    public int VAO { get; private set; }
    public int VertexCount { get; private set; }
    public bool IsIndexed { get; private set; }
    private int _vbo, _ebo;
    private bool _disposed;

    /// <summary>Вершина: 3 float позиция + 3 float нормаль (строго уложено в flat-массив).</summary>
    private const int FloatsPerVertex = 6;
    private const int StrideBytes = FloatsPerVertex * sizeof(float);

    public static Mesh CreateCube()
    {
        // Куб от -1 до +1 по каждой оси (масштабируется модельной матрицей).
        // Каждая грань — свой quad со своей нормалью (плоское освещение).
        var v = new List<float>();

        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var p in new[] { a, b, c, d })
            {
                v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
                v.Add(n.X); v.Add(n.Y); v.Add(n.Z);
            }
        }

        Face(Vector3.UnitZ, new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1));   // front
        Face(-Vector3.UnitZ, new Vector3(1, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1)); // back
        Face(Vector3.UnitX, new Vector3(1, -1, 1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 1));     // right
        Face(-Vector3.UnitX, new Vector3(-1, -1, -1), new Vector3(-1, -1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 1, -1)); // left
        Face(Vector3.UnitY, new Vector3(-1, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1));     // top
        Face(-Vector3.UnitY, new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(-1, -1, 1)); // bottom

        // Индексы: два треугольника на грань, CCW
        var idx = new List<uint>();
        for (int f = 0; f < 6; f++)
        {
            uint o = (uint)(f * 4);
            idx.Add(o); idx.Add(o + 1); idx.Add(o + 2);
            idx.Add(o); idx.Add(o + 2); idx.Add(o + 3);
        }

        return new Mesh(v.ToArray(), idx.ToArray());
    }

    public static Mesh CreateSphere(int stacks = 24, int sectors = 32)
    {
        var verts = new List<float>();
        var indices = new List<uint>();

        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathHelper.Pi * i / stacks;              // 0..pi (север -> юг)
            for (int j = 0; j <= sectors; j++)
            {
                float theta = MathHelper.TwoPi * j / sectors; // 0..2pi
                float sx = MathF.Sin(phi) * MathF.Cos(theta);
                float sy = MathF.Cos(phi);
                float sz = MathF.Sin(phi) * MathF.Sin(theta);

                verts.Add(sx); verts.Add(sy); verts.Add(sz);      // единичная сфера
                verts.Add(sx); verts.Add(sy); verts.Add(sz);      // нормаль == позиция
            }
        }

        int stride = sectors + 1;
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                uint a = (uint)(i * stride + j);
                uint b = a + (uint)stride;
                // Два треугольника квада (CCW снаружи)
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(b); indices.Add(b + 1); indices.Add(a + 1);
            }
        }

        return new Mesh(verts.ToArray(), indices.ToArray());
    }

    /// <summary>Плоскость пола (quad в XZ) с нормалью +Y.</summary>
    public static Mesh CreateFloorQuad(float halfSize)
    {
        var v = new List<float>();
        void P(float x, float z)
        {
            v.Add(x); v.Add(0f); v.Add(z);
            v.Add(0f); v.Add(1f); v.Add(0f);
        }
        P(-halfSize, -halfSize);
        P(halfSize, -halfSize);
        P(halfSize, halfSize);
        P(-halfSize, halfSize);

        var idx = new uint[] { 0, 1, 2, 0, 2, 3 };
        return new Mesh(v.ToArray(), idx);
    }

    private Mesh(float[] vertices, uint[] indices)
    {
        VAO = GL.GenVertexArray();
        GL.BindVertexArray(VAO);

        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        _ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        // layout 0: vec3 position
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, StrideBytes, 0);
        // layout 1: vec3 normal
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, StrideBytes, 3 * sizeof(float));

        VertexCount = indices.Length;
        IsIndexed = true;

        GL.BindVertexArray(0);
    }

    public void Draw()
    {
        GL.BindVertexArray(VAO);
        GL.DrawElements(PrimitiveType.Triangles, VertexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(VAO);
        GC.SuppressFinalize(this);
    }
}
