namespace Spacevors.Domain.Components;

public enum MineSize { Small = 0, Large = 1 }

public readonly record struct EnemyMine(MineSize Size, float Speed, float Angle)
{
    public float Radius => Size == MineSize.Large ? 15f : 7.5f;
}
