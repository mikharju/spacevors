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
    public void SingularNormal_Matches()
    {
        var sets = LitSpriteMatcher.Match(["scout-texture", "scout-normal", "scout-depth"]);

        Assert.Single(sets);
        Assert.Equal("scout", sets[0].Prefix);
        Assert.Equal("scout-texture", sets[0].BaseStem);
        Assert.Equal("scout-normal", sets[0].NormalsStem);
        Assert.Equal("scout-depth", sets[0].DepthStem);
    }

    [Fact]
    public void BothNormalForms_PrefersPlural()
    {
        var sets = LitSpriteMatcher.Match(["a-texture", "a-normals", "a-normal", "a-depth"]);

        Assert.Single(sets);
        Assert.Equal("a-normals", sets[0].NormalsStem);
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

    [Fact]
    public void PlainBase_WithNormalsAndDepth_Matches()
    {
        var sets = LitSpriteMatcher.Match(["large-asteroid-2", "large-asteroid-2-normals", "large-asteroid-2-depth"]);

        Assert.Single(sets);
        Assert.Equal("large-asteroid-2", sets[0].Prefix);
        Assert.Equal("large-asteroid-2", sets[0].BaseStem);
        Assert.Equal("large-asteroid-2-normals", sets[0].NormalsStem);
        Assert.Equal("large-asteroid-2-depth", sets[0].DepthStem);
    }

    [Fact]
    public void TexturePreferredOverPlainBase()
    {
        var sets = LitSpriteMatcher.Match(["a", "a-texture", "a-normals", "a-depth"]);

        Assert.Single(sets);
        Assert.Equal("a-texture", sets[0].BaseStem);
    }

    [Fact]
    public void PlainBase_MissingDepth_NotMatched()
    {
        var sets = LitSpriteMatcher.Match(["x", "x-normals"]);

        Assert.Empty(sets);
    }

    [Fact]
    public void IsMapFile_DetectsNormalAndDepthFiles()
    {
        Assert.True(LitSpriteMatcher.IsMapFile("a-normals"));
        Assert.True(LitSpriteMatcher.IsMapFile("a-normal"));
        Assert.True(LitSpriteMatcher.IsMapFile("a-depth"));
        Assert.False(LitSpriteMatcher.IsMapFile("a-texture"));
        Assert.False(LitSpriteMatcher.IsMapFile("a"));
    }

    [Fact]
    public void VariantStems_CoversAllBaseAndMapForms()
    {
        Assert.Equal(
            new[] { "a", "a-texture", "a-normals", "a-normal", "a-depth" },
            LitSpriteMatcher.VariantStems("a"));
    }

    [Fact]
    public void MixedTextureAndPlainBases_AllMatched()
    {
        var stems = new[]
        {
            "small-1-texture", "small-1-normals", "small-1-depth",
            "large-2", "large-2-normals", "large-2-depth"
        };

        var sets = LitSpriteMatcher.Match(stems);

        Assert.Equal(2, sets.Count);
        Assert.Contains(sets, s => s.Prefix == "small-1" && s.BaseStem == "small-1-texture");
        Assert.Contains(sets, s => s.Prefix == "large-2" && s.BaseStem == "large-2");
    }
}
