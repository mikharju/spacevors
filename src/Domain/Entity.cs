namespace Spacevors.Domain;

public readonly record struct Entity(int Value)
{
    private const int NullValue = -1;

    public static Entity Null { get; } = new(NullValue);

    public bool IsNull => Value == NullValue;
}
