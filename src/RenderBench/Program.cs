using System.Diagnostics;
using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Game;

// Headless render benchmark for lit-sprite draw cost. Run under Xvfb with LIBGL_ALWAYS_SOFTWARE=1.
// Usage: RenderBench            -> all scenarios
//        RenderBench <name>     -> one scenario (e.g. lit-200-L16)
//        RenderBench probe      -> shared shader-block correctness check
public static class Program
{
    const int WindowWidth = 1920;
    const int WindowHeight = 1024;
    // Synthetic bench lights keep the warm colour the oracle shader hard-codes, so L4/L16 still look
    // like the scene the probe checks.
    static readonly System.Numerics.Vector3 BenchLightTint = new(1.0f, 0.72f, 0.42f);

    const int WarmupFrames = 30;
    const int MeasureFrames = 240;

    // CPU time, not wall time, is what makes an A/B readable on a contended host: another tenant can
    // double the wall clock while the drawn work stays byte-identical (the untouched flat path measured
    // 8.5 - 12.5 ms inside three minutes). TotalProcessorTime charges every thread, so it also counts
    // llvmpipe's rasteriser workers.
    static readonly Process Self = Process.GetCurrentProcess();

    public static int Main(string[] args)
    {
        Raylib.InitWindow(WindowWidth, WindowHeight, "RenderBench");
        ImageLoader.LoadAssets();
        Lighting.Init();

        var sprites = CollectLitSprites();
        if (sprites.Count == 0)
        {
            Console.WriteLine("no lit sprites loaded");
            return 1;
        }

        foreach (string arg in args)
        {
            if (arg == "probe") RunProbe(sprites);
            else if (!RunScenario(arg, ParseCount(arg), ParseLights(arg), sprites)) return 1;
        }

        if (args.Length == 0)
            foreach (var (name, spriteCount, lightCount) in Scenarios())
                if (!RunScenario(name, spriteCount, lightCount, sprites)) return 1;

        Lighting.Shutdown();
        ImageLoader.UnloadAssets();
        Raylib.CloseWindow();
        return 0;
    }

    static IEnumerable<(string Name, int SpriteCount, int LightCount)> Scenarios()
    {
        yield return ("flat-50", 50, -1);
        yield return ("flat-200", 200, -1);
        yield return ("flat-800", 800, -1);
        yield return ("lit-50-L0", 50, 0);
        yield return ("lit-200-L0", 200, 0);
        yield return ("lit-800-L0", 800, 0);
        yield return ("lit-200-L4", 200, 4);
        yield return ("lit-200-L16", 200, 16);
    }

    static List<LitSprite> CollectLitSprites()
    {
        var list = new List<LitSprite>();
        if (ImageLoader.AsteroidSmallSprites != null)
            foreach (var s in ImageLoader.AsteroidSmallSprites)
                if (s.Lit is { } lit) list.Add(lit);
        if (ImageLoader.AsteroidLargeSprites != null)
            foreach (var s in ImageLoader.AsteroidLargeSprites)
                if (s.Lit is { } lit) list.Add(lit);
        if (ImageLoader.PlayerShipLitSprites != null)
            list.AddRange(ImageLoader.PlayerShipLitSprites.Values);
        if (ImageLoader.EnemyShipLitSprites != null)
            list.AddRange(ImageLoader.EnemyShipLitSprites.Values);
        return list;
    }

    static int ParseCount(string name) => int.Parse(name.Split('-')[1].Split('_')[0]);
    static int ParseLights(string name) => name.Contains('L') ? int.Parse(name[(name.IndexOf('L') + 1)..]) : -1;

    // Returns false when the scenario could not measure what its name claims: with no lit shader the
    // draws fall back to flat sprites, and the timing then prices nothing while still printing a line.
    static bool RunScenario(string name, int spriteCount, int lightCount, List<LitSprite> sprites)
    {
        if (name.StartsWith("lit", StringComparison.Ordinal) && !Lighting.IsReady)
        {
            Console.WriteLine($"ERROR {name}: lit shader unavailable - the lit path did not run, so this would time the flat fallback");
            return false;
        }

        var layout = BuildLayout(spriteCount);
        var lights = BuildLights(lightCount);
        var timings = new List<(double WallMs, double CpuMs)>();

        for (int frame = 0; frame < WarmupFrames + MeasureFrames; frame++)
        {
            Lighting.BeginFrame(0f, 0f, WindowWidth, WindowHeight);
            foreach (var light in lights)
                Lighting.AddLight(light.Position, light.Radius, light.Intensity, BenchLightTint);

            var cpuBefore = Self.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(15, 15, 25, 255));
            DrawSprites(layout, sprites, flat: lightCount < 0);
            Raylib.EndDrawing();
            sw.Stop();
            double cpuMs = (Self.TotalProcessorTime - cpuBefore).TotalMilliseconds;

            if (frame >= WarmupFrames)
                timings.Add((sw.Elapsed.TotalMilliseconds, cpuMs));
        }

        double wall = timings.Average(t => t.WallMs);
        double cpu = timings.Average(t => t.CpuMs);
        Console.WriteLine($"{name,-14} {wall,7:F3} ms wall  {cpu,7:F3} ms cpu  ({1000.0 / cpu,5:F0} fps if whole frame)");
        return true;
    }

    // Grid layout covering the viewport; each cell gets one sprite at a fixed scale.
    static (float X, float Y, float Width, float Height)[] BuildLayout(int count)
    {
        int cols = (int)Math.Ceiling(Math.Sqrt(count * (double)WindowWidth / WindowHeight));
        int rows = (int)Math.Ceiling((double)count / cols);
        float cellW = (float)WindowWidth / cols;
        float cellH = (float)WindowHeight / rows;
        float size = Math.Min(cellW, cellH) * 0.85f;

        var layout = new (float X, float Y, float Width, float Height)[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            layout[i] = ((col + 0.5f) * cellW - size / 2f, (row + 0.5f) * cellH - size / 2f, size, size);
        }
        return layout;
    }

    static (Vector2 Position, float Radius, float Intensity)[] BuildLights(int count)
    {
        if (count <= 0) return [];
        var lights = new (Vector2, float, float)[count];
        for (int i = 0; i < count; i++)
        {
            // Screen-space positions spread across the viewport, converted to world with cam at origin.
            float sx = WindowWidth * (i + 0.5f) / count;
            float sy = WindowHeight * ((i % 4 + 0.5f) / 4f);
            lights[i] = (new Vector2(sx - WindowWidth / 2f, WindowHeight / 2f - sy), 300f, 1f);
        }
        return lights;
    }

    static void DrawSprites((float X, float Y, float Width, float Height)[] layout, List<LitSprite> sprites, bool flat)
    {
        if (flat)
        {
            for (int i = 0; i < layout.Length; i++)
            {
                var sprite = sprites[i % sprites.Count];
                Raylib.DrawTexturePro(sprite.Base, ToSource(sprite), ToDest(layout[i]), CenterOf(layout[i]), AngleFor(i), Color.White);
            }
            return;
        }

        // Mirrors the game path: group by variant, one shader-mode block per variant.
        var groups = new Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>>();
        for (int i = 0; i < layout.Length; i++)
            LitGroupRenderer.Add(groups, sprites[i % sprites.Count], ToDest(layout[i]), AngleFor(i));

        LitGroupRenderer.Draw(groups);
    }

    static Rectangle ToSource(LitSprite sprite) => new(0f, 0f, sprite.Base.Width, sprite.Base.Height);
    static float AngleFor(int i) => (i * 137.5f) % 360f;
    static Rectangle ToDest((float X, float Y, float Width, float Height) cell) => new(cell.X, cell.Y, cell.Width, cell.Height);
    static System.Numerics.Vector2 CenterOf((float X, float Y, float Width, float Height) cell) => new(cell.Width / 2f, cell.Height / 2f);
    static System.Numerics.Vector2 CenterOf(Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);

    // Frozen copy of the pre-batching shader (per-sprite angleRad uniform, no damage path, no
    // transparent early-out), kept as a pixel oracle: if the current grouped path renders this scene
    // identically, vertex-color angle encoding, variant grouping and the early-out are correct. It has
    // no early-out, so a match also proves skipping transparent texels changes no pixel. The oracle
    // reads the maps in whatever encoding the live path uploads them in - packed normal+depth, and a
    // premultiplied base whose albedo it divides back out - so the encoding itself is checked by
    // diffing screenshots across builds (plans/LIT_SPRITE_RENDERING.md). Delete only when the legacy
    // per-sprite path is gone for good.
    const string LegacyVertexShader = """
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

    const string LegacyFragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        // One packed surface map, like the live shader: RGB = normal, A = depth.
        uniform sampler2D normalDepth;
        uniform float angleRad;

        // Screen space: x right, y down. Light from top-right, slightly toward viewer.
        const vec3 LightDir = normalize(vec3(0.6, -0.6, 0.5));
        const float ShadowUvOffset = 0.02;
        const float ShadowDepthBias = 0.03;
        const float ShadowDiffuseScale = 0.15;

        // Mirrors Lighting's rig: key, fill, highlight, rim. A look change must be copied here too, or
        // the probe reports the change itself as a mismatch.
        const vec3 KeyColor = vec3(1.00, 0.95, 0.86);
        const float KeyStrength = 0.72;
        const vec3 FillColor = vec3(0.45, 0.58, 0.95);
        const float FillStrength = 0.32;
        const vec3 HalfVector = normalize(vec3(0.6, -0.6, 1.5));
        const float SpecularPower = 24.0;
        const float SpecularStrength = 0.32;
        const vec3 RimColor = vec3(0.42, 0.60, 1.0);
        const float RimPower = 3.0;
        const float RimStrength = 0.18;
        const float PointLightShadowScale = 0.5;
        // Matches Lighting.TransparentAlphaCutoff, the divisor floor for the premultiplied base.
        const float TransparentAlphaCutoff = 1.5 / 255.0;

        // Point lights: xy screen pos (GL origin bottom-left), z radius px, w intensity.
        // Slots with zero radius or intensity are skipped, so unused slots stay inert.
        const int MaxPointLights = 16;
        uniform vec4 uLights[MaxPointLights];
        const vec3 PointLightTint = vec3(1.0, 0.72, 0.42);   // the bench's one light colour
        // Caps stacked lights so lit surfaces keep texture detail instead of clipping to white.
        const float MaxPointLightContribution = 1.0f;
        // Lights sit this many px above the scene plane (toward viewer), so flat surfaces still receive light.
        const float PointLightHeight = 80.0f;

        void main()
        {
            vec4 baseColor = texture(texture0, fragTexCoord);
            // Same albedo recovery as the live shader: the base is uploaded premultiplied.
            vec3 albedo = baseColor.rgb / max(baseColor.a, TransparentAlphaCutoff);

            // Normal map is tangent space with +Y up in image; screen y points down.
            vec3 nMap = texture(normalDepth, fragTexCoord).rgb * 2.0 - 1.0;
            vec3 normalLocal = normalize(vec3(nMap.x, -nMap.y, nMap.z));

            float c = cos(angleRad);
            float s = sin(angleRad);
            vec3 normalScreen = normalize(vec3(
                c * normalLocal.x - s * normalLocal.y,
                s * normalLocal.x + c * normalLocal.y,
                normalLocal.z));

            // Self-shadow: a higher surface toward the light blocks it. Depth white = closer to viewer.
            vec2 lightUvDir = vec2(c * LightDir.x + s * LightDir.y, -s * LightDir.x + c * LightDir.y);
            float depthHere = texture(normalDepth, fragTexCoord).a;
            float depthTowardLight = texture(normalDepth, fragTexCoord + lightUvDir * ShadowUvOffset).a;
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

            float specular = pow(max(dot(normalScreen, HalfVector), 0.0), SpecularPower) * SpecularStrength * shadowed;
            float rim = pow(1.0 - normalScreen.z, RimPower) * RimStrength;

            vec3 lit = albedo * (FillColor * FillStrength + KeyColor * (diffuse * KeyStrength))
                     + albedo * PointLightTint * pointLight * mix(1.0, PointLightShadowScale, shadowed)
                     + KeyColor * specular
                     + RimColor * rim;

            finalColor = vec4(lit, baseColor.a) * fragColor;
        }
        """;

    static readonly System.Numerics.Vector4[] InertLights = new System.Numerics.Vector4[16];

    // Renders a 2x2 scene (two variants at two angles each) with the legacy per-sprite-block path and
    // the current grouped path, then compares pixels. No point lights: both paths upload identical
    // inert light arrays, so only angle encoding and map grouping are under test.
    static void RunProbe(List<LitSprite> sprites)
    {
        var s1 = sprites[0];
        var s2 = sprites[Math.Min(3, sprites.Count - 1)];
        (LitSprite Sprite, float AngleDeg)[] scene =
        [
            (s1, 12f), (s2, 75f),
            (s1, 200f), (s2, 311f),
        ];

        var legacy = Raylib.LoadShaderFromMemory(LegacyVertexShader, LegacyFragmentShader);
        if (!Raylib.IsShaderValid(legacy))
        {
            Console.WriteLine("probe: legacy oracle shader failed to compile");
            return;
        }

        var reference = RenderLegacy(scene, legacy);
        var current = RenderCurrent(scene);
        Raylib.ExportImage(reference, "/tmp/probe_legacy.png");
        Raylib.ExportImage(current, "/tmp/probe_current.png");

        int maxDelta = 0;
        long sumDelta = 0, samples = 0;
        for (int y = 0; y < reference.Height; y += 4)
            for (int x = 0; x < reference.Width; x += 4)
            {
                var c1 = Raylib.GetImageColor(reference, x, y);
                var c2 = Raylib.GetImageColor(current, x, y);
                int delta = Math.Max(Math.Abs(c1.R - c2.R), Math.Max(Math.Abs(c1.G - c2.G), Math.Abs(c1.B - c2.B)));
                maxDelta = Math.Max(maxDelta, delta);
                sumDelta += delta;
                samples++;
            }

        Console.WriteLine($"probe: maxChannelDelta={maxDelta} avgChannelDelta={(double)sumDelta / samples:F3}");
        Console.WriteLine(maxDelta <= 8 ? "probe: PASS (grouped path matches legacy per-sprite path)" : "probe: FAIL (compare /tmp/probe_legacy.png vs /tmp/probe_current.png)");

        Raylib.UnloadShader(legacy);
    }

    static Image RenderLegacy((LitSprite Sprite, float AngleDeg)[] scene, Shader legacy)
    {
        Lighting.BeginFrame(0f, 0f, WindowWidth, WindowHeight);
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 15, 25, 255));

        int normalDepthLoc = Raylib.GetShaderLocation(legacy, "normalDepth");
        int angleLoc = Raylib.GetShaderLocation(legacy, "angleRad");
        int lightsLoc = Raylib.GetShaderLocation(legacy, "uLights");

        for (int i = 0; i < scene.Length; i++)
        {
            var (sprite, angleDeg) = scene[i];
            Raylib.BeginShaderMode(legacy);
            Raylib.SetShaderValueTexture(legacy, normalDepthLoc, sprite.NormalDepth);
            Raylib.SetShaderValue(legacy, angleLoc, angleDeg * MathF.PI / 180f, ShaderUniformDataType.Float);
            Raylib.SetShaderValueV(legacy, lightsLoc, InertLights, ShaderUniformDataType.Vec4, InertLights.Length);
            Raylib.DrawTexturePro(sprite.Base, ToSource(sprite), CellRect(i), CenterOf(CellRect(i)), angleDeg, Color.White);
            Raylib.EndShaderMode();
        }

        Raylib.EndDrawing();
        return Raylib.LoadImageFromScreen();
    }

    static Image RenderCurrent((LitSprite Sprite, float AngleDeg)[] scene)
    {
        Lighting.BeginFrame(0f, 0f, WindowWidth, WindowHeight);
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 15, 25, 255));

        var groups = new Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>>();
        for (int i = 0; i < scene.Length; i++)
            LitGroupRenderer.Add(groups, scene[i].Sprite, CellRect(i), scene[i].AngleDeg);
        LitGroupRenderer.Draw(groups);

        Raylib.EndDrawing();
        return Raylib.LoadImageFromScreen();
    }

    static Rectangle CellRect(int i)
    {
        const float Size = 300f;
        float cx = WindowWidth * (i % 2 == 0 ? 0.25f : 0.75f);
        float cy = WindowHeight * (i < 2 ? 0.25f : 0.75f);
        return new Rectangle(cx - Size / 2f, cy - Size / 2f, Size, Size);
    }

}
