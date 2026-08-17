using Raylib_cs;
using Spacevors.Domain;

namespace Spacevors.Game;

public static class Lighting
{
    // Matches raylib's default vertex shader (raylib 5.x attribute layout).
    const string VertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        uniform mat4 mvp;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    const string FragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        uniform sampler2D normalMap;
        uniform sampler2D depthMap;
        uniform float angleRad;

        // Screen space: x right, y down. Light from top-right, slightly toward viewer.
        const vec3 LightDir = normalize(vec3(0.6, -0.6, 0.5));
        const float AmbientLevel = 0.45;
        const float ShadowUvOffset = 0.02;
        const float ShadowDepthBias = 0.03;
        const float ShadowDiffuseScale = 0.4;

        void main()
        {
            vec4 baseColor = texture(texture0, fragTexCoord);

            // Normal map is tangent space with +Y up in image; screen y points down.
            vec3 nMap = texture(normalMap, fragTexCoord).rgb * 2.0 - 1.0;
            vec3 normalLocal = normalize(vec3(nMap.x, -nMap.y, nMap.z));

            float c = cos(angleRad);
            float s = sin(angleRad);
            vec3 normalScreen = normalize(vec3(
                c * normalLocal.x - s * normalLocal.y,
                s * normalLocal.x + c * normalLocal.y,
                normalLocal.z));

            // Self-shadow: a higher surface toward the light blocks it. Depth white = closer to viewer.
            vec2 lightUvDir = vec2(c * LightDir.x + s * LightDir.y, -s * LightDir.x + c * LightDir.y);
            float depthHere = texture(depthMap, fragTexCoord).r;
            float depthTowardLight = texture(depthMap, fragTexCoord + lightUvDir * ShadowUvOffset).r;
            float shadowed = 1.0 - smoothstep(-ShadowDepthBias, ShadowDepthBias, depthHere - depthTowardLight);

            float diffuse = max(dot(normalScreen, LightDir), 0.0) * mix(1.0, ShadowDiffuseScale, shadowed);
            vec3 lit = baseColor.rgb * (AmbientLevel + diffuse);

            finalColor = vec4(lit, baseColor.a) * fragColor;
        }
        """;

    public static bool IsReady { get; private set; }

    static Shader? _shader;
    static int _normalMapLoc;
    static int _depthMapLoc;
    static int _angleRadLoc;

    public static void Init()
    {
        var shader = Raylib.LoadShaderFromMemory(VertexShader, FragmentShader);
        if (!Raylib.IsShaderValid(shader))
        {
            DiagnosticLogger.LogEvent("LIGHTING", "shader failed to compile; falling back to flat sprites");
            return;
        }

        _shader = shader;
        _normalMapLoc = Raylib.GetShaderLocation(shader, "normalMap");
        _depthMapLoc = Raylib.GetShaderLocation(shader, "depthMap");
        _angleRadLoc = Raylib.GetShaderLocation(shader, "angleRad");
        IsReady = true;
    }

    public static void Shutdown()
    {
        if (_shader != null && _shader.Value.Id != 0)
            Raylib.UnloadShader(_shader.Value);
        _shader = null;
        IsReady = false;
    }

    // Draws the lit sprite with the lighting shader. Returns false when the shader is unavailable,
    // so callers can fall back to drawing sprite.Base as a flat texture.
    public static bool TryDraw(LitSprite sprite, Rectangle source, Rectangle dest, System.Numerics.Vector2 origin, float angleDeg)
    {
        if (!IsReady || _shader == null) return false;

        var shader = _shader.Value;
        Raylib.SetShaderValueTexture(shader, _normalMapLoc, sprite.Normals);
        Raylib.SetShaderValueTexture(shader, _depthMapLoc, sprite.Depth);
        Raylib.SetShaderValue(shader, _angleRadLoc, angleDeg * MathF.PI / 180f, ShaderUniformDataType.Float);

        Raylib.BeginShaderMode(shader);
        Raylib.DrawTexturePro(sprite.Base, source, dest, origin, angleDeg, Color.White);
        Raylib.EndShaderMode();
        return true;
    }
}
