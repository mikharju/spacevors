using System.Diagnostics;
using Spacevors.Domain;
using Xunit;

// Repeatable SpatialGrid micro-benchmark: the grid's own cost at the 10k-object goal, plus how
// many unique candidates a dense-cluster query really produces. CollisionSystem starts with a
// 256-entry query buffer and doubles it when a query truncates, so these counts are what the
// buffer has to grow to in a dense cluster.
public class SpatialGridBenchmark
{
    [Fact]
    public void Grid_10kEntities_InsertAndQueryCost()
    {
        const int entities = 10_000;
        const int ticks = 60;
        const int queriesPerTick = 1_000;
        const double budgetMsPerTick = 15.0;

        var rng = new Random(42);
        var grid = new SpatialGrid(128f);
        var buffer = new SpatialGrid.SpatialItem[256];
        var positions = new Vector2[entities];
        for (int i = 0; i < entities; i++)
            positions[i] = new Vector2((float)(rng.NextDouble() * 6000 - 3000), (float)(rng.NextDouble() * 6000 - 3000));

        double insertMs = 0, queryMs = 0;
        var sw = new Stopwatch();
        for (int tick = 0; tick < ticks; tick++)
        {
            sw.Restart();
            grid.Clear();
            for (int i = 0; i < entities; i++)
            {
                positions[i] += new Vector2((float)(rng.NextDouble() * 4 - 2), (float)(rng.NextDouble() * 4 - 2));
                grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, positions[i], 15f);
            }
            sw.Stop();
            insertMs += sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int q = 0; q < queriesPerTick; q++)
                grid.GetQueryItems(positions[rng.Next(entities)], 30f, buffer, out _);
            sw.Stop();
            queryMs += sw.Elapsed.TotalMilliseconds;
        }

        double insertPerTick = insertMs / ticks;
        double queryPerTick = queryMs / ticks;
        Console.WriteLine();
        Console.WriteLine("=== SpatialGrid: 10k entities, cellSize 128 ===");
        Console.WriteLine($"  {"insert + clear",-35} {insertPerTick,6:F2}ms/tick");
        Console.WriteLine($"  {"1000 x r=30 queries",-35} {queryPerTick,6:F2}ms/tick ({queryPerTick * 1000 / queriesPerTick:F2}us/query)");

        double total = insertPerTick + queryPerTick;
        Assert.True(total < budgetMsPerTick, $"grid work {total:F2}ms/tick exceeds {budgetMsPerTick}ms");
    }

    [Fact]
    public void Grid_DenseCluster_QueryBufferHeadroom()
    {
        const int bufferEntries = 256;
        Console.WriteLine();
        Console.WriteLine("=== SpatialGrid: unique candidates per r=22.5 query (initial buffer 256) ===");
        foreach (int mines in new[] { 200, 500, 1000, 3000 })
        {
            var rng = new Random(7);
            var grid = new SpatialGrid(128f);
            for (int i = 0; i < mines; i++)
            {
                double angle = rng.NextDouble() * MathF.PI * 2f;
                double dist = 30 + rng.NextDouble() * 120;
                grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.EnemyMine,
                    new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist)), 7.5f);
            }

            var wide = new SpatialGrid.SpatialItem[4096];
            var probe = new Random(11);
            int maxCandidates = 0;
            for (int q = 0; q < 200; q++)
            {
                double angle = probe.NextDouble() * MathF.PI * 2f;
                double dist = probe.NextDouble() * 150;
                int c = grid.GetQueryItems(new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist)), 22.5f, wide, out _);
                maxCandidates = Math.Max(maxCandidates, c);
            }
            Console.WriteLine($"  {mines,5} mines in r=150 cluster: worst query returns {maxCandidates} unique candidates" +
                              (maxCandidates > bufferEntries ? $" -> buffer grows past {bufferEntries}" : ""));
        }
    }
}
