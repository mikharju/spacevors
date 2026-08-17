using Spacevors.Game;
using Xunit;

public class LitSpriteMatcherTest
{
    [Fact]
    public void CompleteSet_Matches()
    {
        var sets = LitSpriteMatcher.Match(["shadow-texture", "shadow-normals", "shadow-depth"]);

        Assert.Single(sets);
        Assert.Equal("shadow", sets[0].Prefix);
        Assert.Equal("shadow-texture", sets[0].BaseStem);
        Assert.Equal("shadow-normals", sets[0].NormalsStem);
        Assert.Equal("shadow-depth", sets[0].DepthStem);
    }

    [Fact]
    public void MissingNormals_NotMatched()
    {
        var sets = LitSpriteMatcher.Match(["shadow-texture", "shadow-depth"]);

        Assert.Empty(sets);
    }

    [Fact]
    public void MissingDepth_NotMatched()
    {
        var sets = LitSpriteMatcher.Match(["shadow-texture", "shadow-normals"]);

        Assert.Empty(sets);
    }

    [Fact]
    public void PlainSprites_NotMatched()
    {
        var sets = LitSpriteMatcher.Match(["scout", "fighter", "heavy"]);

        Assert.Empty(sets);
    }

    [Fact]
    public void MultipleSets_AllMatched()
    {
        var stems = new[]
        {
            "a-texture", "a-normals", "a-depth",
            "b-texture", "b-normals", "b-depth"
        };

        var sets = LitSpriteMatcher.Match(stems);

        Assert.Equal(2, sets.Count);
        Assert.Contains(sets, s => s.Prefix == "a");
        Assert.Contains(sets, s => s.Prefix == "b");
    }

    [Fact]
    public void EmptyPrefix_NotMatched()
    {
        var sets = LitSpriteMatcher.Match(["-texture", "-normals", "-depth"]);

        Assert.Empty(sets);
    }
}
