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
        const float AmbientLevel = 0.30;
        const float ShadowUvOffset = 0.02;
        const float ShadowDepthBias = 0.03;
        const float ShadowDiffuseScale = 0.15;

        // Point lights: xy screen pos (GL origin bottom-left), z radius px, w intensity.
        // Slots with zero radius or intensity are skipped, so unused slots stay inert.
        const int MaxPointLights = 16;
        uniform vec4 uLights[MaxPointLights];
        const vec3 PointLightTint = vec3(1.0, 0.72, 0.42);
        // Caps stacked lights so lit surfaces keep texture detail instead of clipping to white.
        const float MaxPointLightContribution = 1.0f;
        // Lights sit this many px above the scene plane (toward viewer), so flat surfaces still receive light.
        const float PointLightHeight = 80.0f;

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

            // Point lights add a warm glow on top of the directional light, shaded by sprite normals.
            float pointLight = 0.0;
            for (int i = 0; i < MaxPointLights; i++) {
                vec4 light = uLights[i];
                if (light.w <= 0.0 || light.z <= 0.0) continue;
                float dist = distance(gl_FragCoord.xy, light.xy);
                float fall = 1.0 - smoothstep(0.0, light.z, dist);
                // gl_FragCoord and light positions are GL space (y up); normalScreen is y down.
                vec2 dGl = light.xy - gl_FragCoord.xy;
                vec3 toLight = normalize(vec3(dGl.x, -dGl.y, PointLightHeight));
                float facing = max(dot(normalScreen, toLight), 0.0);
                pointLight += light.w * fall * fall * facing;
            }
            pointLight = min(pointLight, MaxPointLightContribution);

            vec3 lit = baseColor.rgb * (AmbientLevel + diffuse) + PointLightTint * pointLight;

            finalColor = vec4(lit, baseColor.a) * fragColor;
        }
        """;

    // Must match the shader's MaxPointLights.
    const int MaxPointLights = 16;

    public static bool IsReady { get; private set; }

    static Shader? _shader;
    static int _normalMapLoc;
    static int _depthMapLoc;
    static int _angleRadLoc;
    static int _lightsLoc;

    // Frame-scoped point light list, filled by LightGatherer after BeginFrame.
    static readonly System.Numerics.Vector4[] PointLights = new System.Numerics.Vector4[MaxPointLights];
    static int _pointLightCount;
    static float _camX;
    static float _camY;
    static int _windowWidth;
    static int _windowHeight;

    // Resets the light list for a new frame. Must be called once per frame before any lit draw,
    // even on frames with no lights (e.g. ship select), so stale lights do not leak across screens.
    public static void BeginFrame(float camX, float camY, int windowWidth, int windowHeight)
    {
        _camX = camX;
        _camY = camY;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
        Array.Clear(PointLights);
        _pointLightCount = 0;
    }

    // Adds a point light in world coordinates (1 unit = 1 px). Dropped when the list is full.
    public static void AddLight(Vector2 worldPos, float radius, float intensity)
    {
        if (_pointLightCount >= MaxPointLights || radius <= 0f || intensity <= 0f) return;

        float screenX = worldPos.X - _camX + _windowWidth / 2f;
        float screenYGl = _windowHeight - (worldPos.Y - _camY + _windowHeight / 2f);
        PointLights[_pointLightCount++] = new System.Numerics.Vector4(screenX, screenYGl, radius, intensity);
    }

    public static void Init()
    {
        var shader = Raylib.LoadShaderFromMemory(VertexShader, FragmentShader);
        if (!Raylib.IsShaderValid(shader))
        {
            Raylib.UnloadShader(shader);
            DiagnosticLogger.LogEvent("LIGHTING", "shader failed to compile; falling back to flat sprites");
            return;
        }

        _shader = shader;
        _normalMapLoc = Raylib.GetShaderLocation(shader, "normalMap");
        _depthMapLoc = Raylib.GetShaderLocation(shader, "depthMap");
        _angleRadLoc = Raylib.GetShaderLocation(shader, "angleRad");
        _lightsLoc = Raylib.GetShaderLocation(shader, "uLights");
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
        // BeginShaderMode must come first: its batch flush clears raylib's texture-unit registry,
        // so map uniforms set before it are lost on an empty flush and the sprite renders with
        // the previous lit sprite's normal/depth maps.
        Raylib.BeginShaderMode(shader);
        Raylib.SetShaderValueTexture(shader, _normalMapLoc, sprite.Normals);
        Raylib.SetShaderValueTexture(shader, _depthMapLoc, sprite.Depth);
        Raylib.SetShaderValue(shader, _angleRadLoc, angleDeg * MathF.PI / 180f, ShaderUniformDataType.Float);
        Raylib.SetShaderValueV(shader, _lightsLoc, PointLights, ShaderUniformDataType.Vec4, MaxPointLights);

        Raylib.DrawTexturePro(sprite.Base, source, dest, origin, angleDeg, Color.White);
        Raylib.EndShaderMode();
        return true;
    }
}
