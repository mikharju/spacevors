namespace Spacevors.Domain;

public class CommandProcessor
{
    private readonly EntityManager _em;

    public CommandProcessor(EntityManager em)
    {
        _em = em;
    }

    public void Process(IEnumerable<ICommand> commands)
    {
        var destroyEntities = new HashSet<Entity>();

        foreach (var cmd in commands)
        {
            if (cmd is CreateEntityWithComponentsCommand createCmd)
            {
                var entity = _em.CreateEntity();
                foreach (var component in createCmd.InitialComponents)
                    component.Apply(_em, entity);
            }
            else if (cmd is DestroyEntityCommand destroyCmd)
            {
                destroyEntities.Add(destroyCmd.Entity);
            }
            else if (cmd is IApplyCommand applyCmd)
            {
                applyCmd.Apply(_em);
            }
        }

        foreach (var entity in destroyEntities)
        {
            _em.DestroyEntity(entity);
        }
    }
}
