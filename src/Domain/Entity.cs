namespace Spacevors.Domain;

public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Value { get; }

    private const int NullValue = -1;

    public static Entity Null => new(NullValue);

    public bool IsNull => Value == NullValue;

    public Entity(int value)
    {
        Value = value;
    }

    public bool Equals(Entity other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is Entity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value;
    }

    public static bool operator ==(Entity left, Entity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Entity left, Entity right)
    {
        return !left.Equals(right);
    }
}
