using Spacevors.Domain;
using Xunit;

public class Vector2Tests
{
    [Fact]
    public void Zero_HasZeroMagnitude()
    {
        Assert.Equal(0f, Vector2.Zero.Magnitude);
    }

    [Fact]
    public void Magnitude_34_Returns5()
    {
        var v = new Vector2(3f, 4f);
        Assert.True(Math.Abs(v.Magnitude - 5f) < 0.001f);
    }

    [Fact]
    public void Normalized_HasMagnitudeOne()
    {
        var v = new Vector2(3f, 4f).Normalized;
        Assert.True(Math.Abs(v.Magnitude - 1f) < 0.001f);
    }

    [Fact]
    public void Addition_Correct()
    {
        var a = new Vector2(1f, 2f) + new Vector2(3f, 4f);
        Assert.Equal(4f, a.X);
        Assert.Equal(6f, a.Y);
    }

    [Fact]
    public void Subtraction_Correct()
    {
        var a = new Vector2(5f, 7f) - new Vector2(2f, 3f);
        Assert.Equal(3f, a.X);
        Assert.Equal(4f, a.Y);
    }

    [Fact]
    public void ScalarMultiplication_Correct()
    {
        var v = new Vector2(2f, 3f) * 3f;
        Assert.Equal(6f, v.X);
        Assert.Equal(9f, v.Y);
    }

    [Fact]
    public void DotProduct_OrthogonalVectors_ReturnsZero()
    {
        var dot = Vector2.Dot(new Vector2(1f, 0f), new Vector2(0f, 1f));
        Assert.Equal(0f, dot);
    }

    [Fact]
    public void DotProduct_ParallelVectors_ReturnsProduct()
    {
        var dot = Vector2.Dot(new Vector2(3f, 4f), new Vector2(5f, 0f));
        Assert.Equal(15f, dot);
    }

    [Fact]
    public void Normalized_ZeroVector_ReturnsZero()
    {
        var v = Vector2.Zero.Normalized;
        Assert.Equal(Vector2.Zero.X, v.X);
        Assert.Equal(Vector2.Zero.Y, v.Y);
    }

    [Fact]
    public void Division_Correct()
    {
        var v = new Vector2(6f, 9f) / 3f;
        Assert.Equal(2f, v.X);
        Assert.Equal(3f, v.Y);
    }
}
