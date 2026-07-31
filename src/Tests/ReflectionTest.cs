using Spacevors.Domain;
using Spacevors.Domain.Components;
using System.Reflection;
using Xunit;

namespace Tests;

public class ReflectionTest
{
    [Fact]
    public void TestAddComponentDirectly()
    {
        var em = new EntityManager();
        var entity = em.CreateEntity();
        
        em.AddComponent(entity, new Position(new Vector2(10f, 20f)));
        
        var positions = em.GetEntitiesWithComponents<Position>().ToList();
        Assert.True(positions.Count == 1, $"Expected 1 but got {positions.Count}");
    }
    
    [Fact]
    public void TestAddComponentViaReflectionBoxed()
    {
        var em = new EntityManager();
        var entity = em.CreateEntity();
        
        object boxedComponent = new Position(new Vector2(10f, 20f));
        var componentType = boxedComponent.GetType();
        
        var method = typeof(EntityManager)
            .GetMethod(nameof(EntityManager.AddComponent))!
            .MakeGenericMethod(componentType);
        
        method.Invoke(em, new object[] { entity, boxedComponent });
        
        var positions = em.GetEntitiesWithComponents<Position>().ToList();
        Assert.True(positions.Count == 1, $"Expected 1 but got {positions.Count}");
    }
    
    [Fact]
    public void TestCreateEntityAndAddComponents()
    {
        var em = new EntityManager();
        
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(new Vector2(10f, 20f)));
        em.AddComponent(entity, new Ammo(new Vector2(100f, 100f), 3f, 2f));
        
        var ammoEntities = em.GetEntitiesWithComponents<Ammo>().ToList();
        Assert.True(ammoEntities.Count() == 1, $"Expected 1 but got {ammoEntities.Count()}");
    }
}
