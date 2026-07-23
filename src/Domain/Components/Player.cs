namespace Spacevors.Domain.Components;

public readonly record struct Player(
    float Thrust,
    float SideThrust,
    float BackThrust,
    float Boost,
    float Radius = 18f,
    int Xp = 0,
    int Level = 1,
    float PickupRadius = 60f);

