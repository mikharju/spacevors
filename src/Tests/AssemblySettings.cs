using Xunit;

// ScenarioPerformanceTest times the simulation, and xunit runs test classes in parallel by default,
// so on a small CI runner the measured ticks inherit every other class's work. The suite finishes in
// about a second either way, so serializing it buys timing stability at no practical cost.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
