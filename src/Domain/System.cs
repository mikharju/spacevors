namespace Spacevors.Domain;

public abstract class GameSystem
{
    public abstract void Update(EntityManager em, float deltaTime);
}
