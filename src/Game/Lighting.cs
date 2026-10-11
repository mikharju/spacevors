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

    // Point light slots per frame. The shader source is generated with this value, so the C# and
    // GLSL limits cannot drift apart.
    const int MaxPointLights = 16;

    static readonly string FragmentShader = $$"""
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        uniform sampler2D normalMap;
        uniform sampler2D depthMap;
        uniform float uTime;

        const float TwoPi = 6.28318530718;

        // Screen space: x right, y down. Light from top-right, slightly toward viewer.
        const vec3 LightDir = normalize(vec3(0.6, -0.6, 0.5));
        const float AmbientLevel = 0.30;
        const float ShadowUvOffset = 0.02;
        const float ShadowDepthBias = 0.03;
        const float ShadowDiffuseScale = 0.15;

        // Point lights: xy screen pos (GL origin bottom-left), z radius px, w intensity.
        // Slots with zero radius or intensity are skipped, so unused slots stay inert.
        const int MaxPointLights = {{MaxPointLights}};
        uniform vec4 uLights[MaxPointLights];
        const vec3 PointLightTint = vec3(1.0, 0.72, 0.42);
        // Caps stacked lights so lit surfaces keep texture detail instead of clipping to white.
        const float MaxPointLightContribution = 1.0f;
        // Lights sit this many px above the scene plane (toward viewer), so flat surfaces still receive light.
        const float PointLightHeight = 80.0f;

        // Hull damage rides in vertex color B (0 = pristine: every damage line below is skipped,
        // so undamaged sprites execute the identical path as before damage existed).
        const float DamageGrid = 4.0;  // impact cells per sprite edge
        const vec3 SootColor = vec3(0.10, 0.09, 0.08);
        const vec3 EmberColor = vec3(1.0, 0.35, 0.08);
        // Holes and ember glow start once damage passes this and grow toward 1.
        const float HoleStartDamage = 0.55;

        // Cheap hash (Dave Hoskins) — deterministic per Voronoi cell, seeded away from the origin.
        vec2 hash21(vec2 p)
        {
            p += vec2(31.7, 11.3);
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.xx + p3.yz) * p3.zy);
        }

        void main()
        {
            // Per-sprite rotation is packed into the vertex color: RG hold a 16-bit fixed-point
            // angle over [0, 2pi), B holds hull damage, A holds opacity. Shader uniforms are
            // global at batch-flush time in this raylib build, so per-instance data must travel
            // with the vertices.
            int rByte = int(round(fragColor.r * 255.0));
            int gByte = int(round(fragColor.g * 255.0));
            float angleRad = float(rByte * 256 + gByte) / 65535.0 * TwoPi;
            float damage = fragColor.b;

            vec4 baseColor = texture(texture0, fragTexCoord);

            // Normal map is tangent space with +Y up in image; screen y points down.
            vec3 nMap = texture(normalMap, fragTexCoord).rgb * 2.0 - 1.0;
            vec3 normalLocal = normalize(vec3(nMap.x, -nMap.y, nMap.z));
            float depthDamage = 0.0;
            vec3 ember = vec3(0.0);

            if (damage > 0.0)
            {
                // Procedural hull damage in sprite-local UV space: the pattern rotates with the
                // hull while lighting stays screen-space, so a turning ship keeps its lit and
                // shadow sides. Pure function of uv — no textures, no RNG.
                float hull = step(0.5, baseColor.a);
                vec2 uv = fragTexCoord * DamageGrid;
                vec2 cell = floor(uv);
                vec2 f = fract(uv);

                // Nearest Voronoi cell center = nearest impact site.
                float bestSq = 1e9;
                vec2 bestOff = vec2(0.0);
                vec2 bestId = cell;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++) {
                    vec2 o = vec2(float(i), float(j));
                    vec2 center = o + hash21(cell + o);
                    vec2 d = center - f;
                    float sq = dot(d, d);
                    if (sq < bestSq) { bestSq = sq; bestOff = d; bestId = cell + o; }
                }
                float distC = sqrt(bestSq);
                vec2 cellRand = hash21(bestId);

                // Impacts appear one by one as damage grows; craters widen with damage.
                float impact = step(hash21(bestId + 7.7).x, min(1.0, damage * 1.3));
                float craterR = mix(0.18, 0.55, damage) * (0.7 + 0.6 * cellRand.x);
                float crater = hull * impact * smoothstep(craterR, craterR * 0.25, distC);
                float scorch = hull * impact * smoothstep(craterR * 2.6, craterR * 0.5, distC);

                baseColor.rgb = mix(baseColor.rgb, SootColor, scorch * mix(0.45, 0.8, damage));

                // Perforation: interior impacts punch alpha holes once damage is critical,
                // so space shows through; the hole rotates with the hull, the rest stays lit.
                vec2 centerUv = (bestId + cellRand) / DamageGrid;
                float interior = step(0.12, centerUv.x) * step(centerUv.x, 0.88)
                               * step(0.12, centerUv.y) * step(centerUv.y, 0.88);
                float holeR = interior * impact * max(0.0, damage - HoleStartDamage) / (1.0 - HoleStartDamage) * 0.22;
                if (hull > 0.0 && distC < holeR) discard;

                // Embers: craters glow hot when critical, strongest on the rim around a hole.
                // Additive and small — the hull keeps its directional shading underneath.
                float holeRim = hull * (holeR > 0.0 ? smoothstep(holeR * 2.2, holeR, distC) : 0.0);
                float pulse = 0.75 + 0.25 * sin(uTime * 2.5 + cellRand.y * 6.28318);
                ember = EmberColor * (crater * smoothstep(0.5, 1.0, damage) + holeRim) * 0.45 * damage * pulse;

                // Crater dent: tilt the normal toward the impact center and sink the depth so the
                // existing diffuse and self-shadow passes shade it like a real depression.
                normalLocal = normalize(normalLocal + vec3(bestOff / max(distC, 1e-4), 0.0) * crater * 0.8);
                depthDamage = crater * 0.25;
            }

            float c = cos(angleRad);
            float s = sin(angleRad);
            vec3 normalScreen = normalize(vec3(
                c * normalLocal.x - s * normalLocal.y,
                s * normalLocal.x + c * normalLocal.y,
                normalLocal.z));

            // Self-shadow: a higher surface toward the light blocks it. Depth white = closer to viewer.
            vec2 lightUvDir = vec2(c * LightDir.x + s * LightDir.y, -s * LightDir.x + c * LightDir.y);
            float depthHere = texture(depthMap, fragTexCoord).r - depthDamage;
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

            vec3 lit = baseColor.rgb * (AmbientLevel + diffuse) + PointLightTint * pointLight + ember;

            // RGB of the vertex color carries the packed angle, so only its alpha tints.
            finalColor = vec4(lit, baseColor.a * fragColor.a);
        }
        """;

    public static bool IsReady { get; private set; }

    static Shader? _shader;
    static int _normalMapLoc;
    static int _depthMapLoc;
    static int _lightsLoc;
    static int _timeLoc;

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

        // The shader works in GL screen space (origin bottom-left), so only y is flipped here.
        var screen = RenderHelpers.ToScreen(worldPos, _camX, _camY, _windowWidth, _windowHeight);
        PointLights[_pointLightCount++] = new System.Numerics.Vector4(screen.X, _windowHeight - screen.Y, radius, intensity);
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
        _lightsLoc = Raylib.GetShaderLocation(shader, "uLights");
        _timeLoc = Raylib.GetShaderLocation(shader, "uTime");
        IsReady = true;
    }

    public static void Shutdown()
    {
        if (_shader != null && _shader.Value.Id != 0)
            Raylib.UnloadShader(_shader.Value);
        _shader = null;
        IsReady = false;
    }

    // Begins a block of lit draws that all share one sprite variant (one normal/depth map pair).
    // Shader uniforms are global at batch-flush time in this raylib build, so mixing variants in
    // one block renders earlier sprites with later ones' maps. Returns false when the shader is
    // unavailable; callers then draw flat fallbacks for the whole group.
    public static bool BeginDraw(LitSprite sprite)
    {
        if (!IsReady || _shader == null) return false;

        var shader = _shader.Value;
        // BeginShaderMode must come first: its batch flush clears raylib's texture-unit registry,
        // so map uniforms set before it are lost on an empty flush.
        Raylib.BeginShaderMode(shader);
        Raylib.SetShaderValueTexture(shader, _normalMapLoc, sprite.Normals);
        Raylib.SetShaderValueTexture(shader, _depthMapLoc, sprite.Depth);
        Raylib.SetShaderValueV(shader, _lightsLoc, PointLights, ShaderUniformDataType.Vec4, MaxPointLights);
        // Frame-global time drives the ember pulse; a global uniform is exactly right here.
        Raylib.SetShaderValue(shader, _timeLoc, (float)Raylib.GetTime(), ShaderUniformDataType.Float);
        return true;
    }

    // Draws one lit sprite inside a BeginDraw/EndDraw block. The rotation angle and hull damage
    // are packed into the vertex color (see shader) because per-instance uniforms would leak
    // across draws in the block. damage is [0,1]; 0 renders exactly like an undamaged hull.
    public static void Draw(LitSprite sprite, Rectangle dest, float angleDeg, float damage = 0f)
        => Raylib.DrawTexturePro(sprite.Base, RenderHelpers.FullSource(sprite.Base), dest, RenderHelpers.CenterOrigin(dest), angleDeg, EncodeAngle(angleDeg, damage));

    public static void EndDraw()
    {
        if (_shader != null) Raylib.EndShaderMode();
    }

    // Packs a rotation angle into the vertex color RG channels as 16-bit fixed-point over [0, 360)
    // and the hull damage scalar [0,1] into the B channel.
    static Color EncodeAngle(float angleDeg, float damage)
    {
        float normalized = ((angleDeg % 360f) + 360f) % 360f / 360f;
        int packed = (int)MathF.Round(normalized * 65535f);
        int damageByte = (int)MathF.Round(Math.Clamp(damage, 0f, 1f) * 255f);
        return new Color(packed >> 8, packed & 0xFF, damageByte, 255);
    }
}
