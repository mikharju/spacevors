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
        for (int i = 0; i < layout.Length; i++)
        {
            var sprite = sprites[i % sprites.Count];
            if (!flat && Lighting.TryDraw(sprite, ToSource(sprite), ToDest(layout[i]), CenterOf(layout[i]), AngleFor(i)))
                continue;

            Raylib.DrawTexturePro(
                sprite.Base,
                ToSource(sprite),
                ToDest(layout[i]),
                CenterOf(layout[i]),
                AngleFor(i), Color.White);
        }
    }

    static Rectangle ToSource(LitSprite sprite) => new(0f, 0f, sprite.Base.Width, sprite.Base.Height);
    static float AngleFor(int i) => (i * 137.5f) % 360f;
    static Rectangle ToDest((float X, float Y, float Width, float Height) cell) => new(cell.X, cell.Y, cell.Width, cell.Height);
    static System.Numerics.Vector2 CenterOf((float X, float Y, float Width, float Height) cell) => new(cell.Width / 2f, cell.Height / 2f);

    // Compares a shared shader-mode block render against per-sprite-block reference renders to find
    // which uniforms leak across draws inside one block. Sub-tests:
    //   A: two different sprites (maps + angle differ)  -> do normal/depth maps leak?
    //   B: same sprite, angles 0 and 90                 -> does angleRad leak within a variant group?
    //   C: same sprite, same angle                      -> sanity check, must pass
    static void RunProbe(List<LitSprite> sprites)
    {
        var s1 = sprites[0];
        var s2 = sprites[Math.Min(3, sprites.Count - 1)];

        Compare("A different-sprites", RenderPair(s1, 0f, s2, 45f));
        Compare("B same-sprite-angles", RenderPair(s1, 0f, s1, 90f));
        Compare("C same-everything", RenderPair(s1, 0f, s1, 0f));

        static void Compare(string label, (Image Reference, Image Shared) pair)
        {
            int maxDelta = 0, leftMax = 0, rightMax = 0;
            long sumDelta = 0, leftSum = 0, rightSum = 0;
            int samples = 0, leftSamples = 0, rightSamples = 0;
            for (int y = 0; y < pair.Reference.Height; y += 4)
            {
                for (int x = 0; x < pair.Reference.Width; x += 4)
                {
                    var c1 = Raylib.GetImageColor(pair.Reference, x, y);
                    var c2 = Raylib.GetImageColor(pair.Shared, x, y);
                    int delta = Math.Max(Math.Abs(c1.R - c2.R), Math.Max(Math.Abs(c1.G - c2.G), Math.Abs(c1.B - c2.B)));
                    maxDelta = Math.Max(maxDelta, delta);
                    sumDelta += delta;
                    samples++;
                    if (x < pair.Reference.Width / 2) { leftMax = Math.Max(leftMax, delta); leftSum += delta; leftSamples++; }
                    else { rightMax = Math.Max(rightMax, delta); rightSum += delta; rightSamples++; }
                }
            }

            Console.WriteLine($"probe-{label}: max={maxDelta} avg={(double)sumDelta / samples:F3} | left max={leftMax} avg={(double)leftSum / leftSamples:F3} | right max={rightMax} avg={(double)rightSum / rightSamples:F3}");
        }

        static (Image Reference, Image Shared) RenderPair(LitSprite left, float leftAngle, LitSprite right, float rightAngle)
        {
            var reference = Render(left, leftAngle, right, rightAngle, sharedBlock: false);
            var shared = Render(left, leftAngle, right, rightAngle, sharedBlock: true);
            return (reference, shared);
        }

        static Image Render(LitSprite left, float leftAngle, LitSprite right, float rightAngle, bool sharedBlock)
        {
            Lighting.BeginFrame(0f, 0f, WindowWidth, WindowHeight);
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(15, 15, 25, 255));

            var leftRect = new Rectangle(WindowWidth * 0.25f - 150f, WindowHeight / 2f - 150f, 300f, 300f);
            var rightRect = new Rectangle(WindowWidth * 0.75f - 150f, WindowHeight / 2f - 150f, 300f, 300f);

            if (sharedBlock)
            {
                Raylib.BeginShaderMode(Lighting.ActiveShader);
                SetUniforms(left, leftAngle);
                DrawLitRaw(left, leftRect, leftAngle);
                SetUniforms(right, rightAngle);
                DrawLitRaw(right, rightRect, rightAngle);
                Raylib.EndShaderMode();
            }
            else
            {
                Raylib.BeginShaderMode(Lighting.ActiveShader);
                SetUniforms(left, leftAngle);
                DrawLitRaw(left, leftRect, leftAngle);
                Raylib.EndShaderMode();

                Raylib.BeginShaderMode(Lighting.ActiveShader);
                SetUniforms(right, rightAngle);
                DrawLitRaw(right, rightRect, rightAngle);
                Raylib.EndShaderMode();
            }

            Raylib.EndDrawing();
            return Raylib.LoadImageFromScreen();
        }

        static void SetUniforms(LitSprite sprite, float angleDeg)
        {
            Raylib.SetShaderValueTexture(Lighting.ActiveShader, Lighting.NormalMapLocation, sprite.Normals);
            Raylib.SetShaderValueTexture(Lighting.ActiveShader, Lighting.DepthMapLocation, sprite.Depth);
            Raylib.SetShaderValue(Lighting.ActiveShader, Lighting.AngleRadLocation, angleDeg * MathF.PI / 180f, ShaderUniformDataType.Float);
        }

        static void DrawLitRaw(LitSprite sprite, Rectangle dest, float angleDeg) => Raylib.DrawTexturePro(
            sprite.Base,
            new Rectangle(0f, 0f, sprite.Base.Width, sprite.Base.Height),
            dest,
            new System.Numerics.Vector2(dest.Width / 2f, dest.Height / 2f),
            angleDeg, Color.White);
    }
}
