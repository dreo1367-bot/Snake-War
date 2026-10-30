namespace BrickBreaker3D;

/// <summary>
/// Встроенные копии GLSL-шейдеров (fallback, если папка shaders/ не найдена рядом с EXE).
/// Содержимое идентично shaders/model.vert и shaders/model.frag.
/// </summary>
public static class EmbeddedShaders
{
    public const string Vertex = """
		#version 330 core

		layout (location = 0) in vec3 aPos;
		layout (location = 1) in vec3 aNormal;

		uniform mat4 uModel;
		uniform mat4 uView;
		uniform mat4 uProjection;

		out vec3 vWorldPos;
		out vec3 vNormal;

		void main()
		{
		    vec4 world = uModel * vec4(aPos, 1.0);
		    vWorldPos = world.xyz;
		    // Равномерное масштабирование -> нормали можно не инвертировать трансформацией
		    vNormal = mat3(uModel) * aNormal;
		    gl_Position = uProjection * uView * world;
		}
""";

    public const string Fragment = """
		#version 330 core

		in vec3 vWorldPos;
		in vec3 vNormal;

		out vec4 FragColor;

		uniform vec3 uViewPos;      // позиция камеры (для диффузного света по Бланну-Френелю)
		uniform vec3 uLightPos;     // позиция источника света
		uniform vec3 uObjectColor;  // сплошной цвет объекта (белый/серый)

		void main()
		{
		    vec3 norm = normalize(vNormal);
		    if (!gl_FrontFacing)
		        norm = -norm;

		    // --- Диффузный свет (чтобы грани куба различались) ---
		    vec3 lightDir = normalize(uLightPos - vWorldPos);
		    float diff = max(dot(norm, lightDir), 0.0);

		    // --- Спекулярный блик (лёгкий, для читаемости сфер и рёбер) ---
		    vec3 viewDir = normalize(uViewPos - vWorldPos);
		    vec3 halfDir = normalize(lightDir + viewDir);
		    float spec = pow(max(dot(norm, halfDir), 0.0), 24.0) * 0.25;

		    // --- Полусферическое окружение: чуть подкрашиваем нижние грани ---
		    float ambientHalf = 0.18 + 0.12 * (norm.y * 0.5 + 0.5);

		    // --- Лёгкий туман по расстоянию, чтобы сцена читалась в глубину ---
		    float dist = length(uLightPos - vWorldPos);
		    float fog = clamp(1.0 - dist / 90.0, 0.55, 1.0);

		    vec3 base = uObjectColor;
		    vec3 color = base * (ambientHalf + 0.85 * diff) + vec3(spec);
		    color *= fog;

		    FragColor = vec4(color, 1.0);
		}
""";
}
