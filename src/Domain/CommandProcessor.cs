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
        // List, not HashSet: destroys then apply in command order, making the resulting
        // storage layout contractual instead of relying on HashSet iteration behavior.
        // Destroy counts per flush are small, so the Contains dedup stays cheap.
        var destroyEntities = new List<Entity>();

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
                if (!destroyEntities.Contains(destroyCmd.Entity))
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
