using System.Reflection;

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
                foreach (var component in createCmd.Components)
                {
                    var componentType = component.GetType();
                    var method = typeof(EntityManager)
                        .GetMethod(nameof(EntityManager.AddComponent))!
                        .MakeGenericMethod(componentType);
                    method.Invoke(_em, new object[] { entity, component });
                }
            }
            else if (cmd.GetType().IsGenericType && cmd.GetType().GetGenericTypeDefinition() == typeof(AddComponentCommand<>))
            {
                var entityProp = cmd.GetType().GetProperty("Entity");
                var componentProp = cmd.GetType().GetProperty("Component");
                var entity = entityProp!.GetValue(cmd);
                var component = componentProp!.GetValue(cmd);
                ProcessAddComponent((Entity)entity!, component!);
            }
            else if (cmd is DestroyEntityCommand destroyCmd)
            {
                destroyEntities.Add(destroyCmd.Entity);
            }
        }

        foreach (var entity in destroyEntities)
        {
            _em.DestroyEntity(entity);
        }
    }

    private void ProcessAddComponent(Entity entity, object component)
    {
        var componentType = component.GetType();
        var method = typeof(EntityManager)
            .GetMethod(nameof(EntityManager.AddComponent))!
            .MakeGenericMethod(componentType);
        method.Invoke(_em, new object[] { entity, component });
    }
}
