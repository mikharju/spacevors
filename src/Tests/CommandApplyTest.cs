using Spacevors.Domain;
using Spacevors.Domain.Components;
using Xunit;

namespace Tests;

public class CommandApplyTest
{
    [Fact]
    public void TestCommandBufferApply()
    {
        var em = new EntityManager();
        
        var commands = new CommandBuffer();
        commands.AddEntity(new Position(new Vector2(10f, 20f)), new Ammo(new Vector2(100f, 100f), 3f, 2f));
        
        commands.Apply(em);
        
        var ammoEntities = em.GetEntitiesWithComponents<Ammo>().ToList();
        Assert.True(ammoEntities.Count() == 1, $"Expected 1 Ammo entity but got {ammoEntities.Count()}");
    }
    
    [Fact]
    public void TestCommandProcessorDirectly()
    {
        var em = new EntityManager();
        
        // First verify that AddComponent works directly
        var testEntity = em.CreateEntity();
        em.AddComponent(testEntity, new Ammo(new Vector2(100f, 100f), 3f, 2f));
        var directAmmo = em.GetEntitiesWithComponents<Ammo>().ToList();
        Assert.True(directAmmo.Count() == 1, $"Direct AddComponent failed: got {directAmmo.Count()}");
        
        // Now test via CommandProcessor
        var em2 = new EntityManager();
        ICommand[] commands = 
        [
            new CreateEntityWithComponentsCommand(
                new InitialComponent<Position>(new Position(new Vector2(10f, 20f))),
                new InitialComponent<Ammo>(new Ammo(new Vector2(100f, 100f), 3f, 2f))
            )
        ];
        
        var processor = new CommandProcessor(em2);
        processor.Process(commands);
        
        var ammoEntities = em2.GetEntitiesWithComponents<Ammo>().ToList();
        Assert.True(ammoEntities.Count() == 1, $"CommandProcessor failed: got {ammoEntities.Count()}");
    }
}
