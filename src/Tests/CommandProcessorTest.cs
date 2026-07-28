using Spacevors.Domain;
using Spacevors.Domain.Components;
using Xunit;

namespace Tests;

public class CommandProcessorTest
{
    [Fact]
    public void TestCreateEntityWithComponents()
    {
        var em = new EntityManager();
        
        // Create an entity with Position and Ammo using CommandBuffer
        var commands = new CommandBuffer();
        commands.Add(new CreateEntityWithComponentsCommand(
            new Position(new Vector2(10f, 20f)),
            new Velocity(new Vector2(5f, 5f)),
            new Ammo(new Vector2(100f, 100f), 3f, 2f, false, 1)
        ));
        
        commands.Apply(em);
        
        // Verify entity was created with all components
        var ammoEntities = em.GetEntitiesWith<Ammo>().ToList();
        Assert.Single(ammoEntities);
        
        var ammoComponents = em.GetEntitiesWithComponents<Ammo>().ToList();
        Assert.Single(ammoComponents);
        
        var (entity, ammo) = ammoComponents[0];
        Assert.Equal(3f, ammo.Radius);
        Assert.Equal(2f, ammo.Lifetime);
    }
}
