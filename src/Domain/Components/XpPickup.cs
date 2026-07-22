namespace Spacevors.Domain.Components;

public readonly record struct XpPickup(int XpAmount, float Lifetime = 30f, float Radius = 6f, bool Chased = false);
