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
    const int WarmupFrames = 30;
    const int MeasureFrames = 240;

    static readonly Dictionary<string, List<double>> Timings = new();

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
            else RunScenario(arg, ParseCount(arg), ParseLights(arg), sprites);
        }

        if (args.Length == 0)
            foreach (var (name, spriteCount, lightCount) in Scenarios())
                RunScenario(name, spriteCount, lightCount, sprites);

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

    static void RunScenario(string name, int spriteCount, int lightCount, List<LitSprite> sprites)
    {
        var layout = BuildLayout(spriteCount);
        var lights = BuildLights(lightCount);

        for (int frame = 0; frame < WarmupFrames + MeasureFrames; frame++)
        {
            Lighting.BeginFrame(0f, 0f, WindowWidth, WindowHeight);
            foreach (var light in lights)
                Lighting.AddLight(light.Position, light.Radius, light.Intensity);

            var sw = Stopwatch.StartNew();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(15, 15, 25, 255));
            DrawSprites(layout, sprites, flat: lightCount < 0);
            Raylib.EndDrawing();
            sw.Stop();

            if (frame >= WarmupFrames)
                Accumulate(name, sw.Elapsed.TotalMilliseconds);
        }

        double avg = Timings[name].Average();
        Console.WriteLine($"{name,-14} {avg,7:F3} ms/frame  ({(1000.0 / avg):5.0} fps if whole frame)");
    }

    static void Accumulate(string name, double ms)
    {
        if (!Timings.TryGetValue(name, out var list))
            Timings[name] = list = new List<double>();
        list.Add(ms);
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
        var groups = new Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>>();
        for (int i = 0; i < layout.Length; i++)
            LitGroupRenderer.Add(groups, sprites[i % sprites.Count], ToDest(layout[i]), AngleFor(i));

        LitGroupRenderer.Draw(groups);
    }

    static Rectangle ToSource(LitSprite sprite) => new(0f, 0f, sprite.Base.Width, sprite.Base.Height);
    static float AngleFor(int i) => (i * 137.5f) % 360f;
    static Rectangle ToDest((float X, float Y, float Width, float Height) cell) => new(cell.X, cell.Y, cell.Width, cell.Height);
    static System.Numerics.Vector2 CenterOf((float X, float Y, float Width, float Height) cell) => new(cell.Width / 2f, cell.Height / 2f);
    static System.Numerics.Vector2 CenterOf(Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);

    // Frozen copy of the pre-batching shader (per-sprite angleRad uniform), kept as a pixel oracle:
    // if the current grouped path renders this scene identically, vertex-color angle encoding and
    // variant grouping are correct. Delete only when the legacy per-sprite path is gone for good.
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

        int normalLoc = Raylib.GetShaderLocation(legacy, "normalMap");
        int depthLoc = Raylib.GetShaderLocation(legacy, "depthMap");
        int angleLoc = Raylib.GetShaderLocation(legacy, "angleRad");
        int lightsLoc = Raylib.GetShaderLocation(legacy, "uLights");

        for (int i = 0; i < scene.Length; i++)
        {
            var (sprite, angleDeg) = scene[i];
            Raylib.BeginShaderMode(legacy);
            Raylib.SetShaderValueTexture(legacy, normalLoc, sprite.Normals);
            Raylib.SetShaderValueTexture(legacy, depthLoc, sprite.Depth);
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

        var groups = new Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>>();
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
