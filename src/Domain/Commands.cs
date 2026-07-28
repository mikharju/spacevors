namespace Spacevors.Domain;

public interface ICommand { }

/// <summary>
/// A component to be added when creating a new entity. Applied with the newly created Entity.
/// </summary>
public interface IInitialComponent
{
    void Apply(EntityManager em, Entity entity);
}

/// <summary>
/// Creates a new entity and adds all initial components to it.
/// </summary>
public readonly record struct CreateEntityWithComponentsCommand : ICommand
{
    public IReadOnlyList<IInitialComponent> InitialComponents { get; }

    public CreateEntityWithComponentsCommand(params IInitialComponent[] components)
    {
        InitialComponents = components;
    }
}

/// <summary>
/// Adds a component to an existing entity.
/// </summary>
public interface IApplyCommand : ICommand
{
    void Apply(EntityManager em);
}

/// <summary>
/// Adds a component to an existing entity.
/// </summary>
public readonly record struct AddComponentCommand<T>(Entity TargetEntity, T Component) : IApplyCommand
    where T : notnull
{
    public void Apply(EntityManager em)
    {
        em.AddComponent(TargetEntity, Component);
    }
}

public readonly record struct DestroyEntityCommand : ICommand
{
    public Entity Entity { get; }

    public DestroyEntityCommand(Entity entity)
    {
        Entity = entity;
    }
}

public class CommandBuffer
{
    private readonly List<ICommand> _commands = new();

    public void Add<T>(T command) where T : ICommand
    {
        _commands.Add(command);
    }

    /// <summary>
    /// Creates a new entity with the given components and adds it to the buffer.
    /// </summary>
    public void AddEntity<T1, T2>(T1 c1, T2 c2) where T1 : notnull where T2 : notnull
        => Add(new CreateEntityWithComponentsCommand(
            new InitialComponent<T1>(c1),
            new InitialComponent<T2>(c2)));

    /// <summary>
    /// Creates a new entity with the given components and adds it to the buffer.
    /// </summary>
    public void AddEntity<T1, T2, T3>(T1 c1, T2 c2, T3 c3) where T1 : notnull where T2 : notnull where T3 : notnull
        => Add(new CreateEntityWithComponentsCommand(
            new InitialComponent<T1>(c1),
            new InitialComponent<T2>(c2),
            new InitialComponent<T3>(c3)));

    /// <summary>
    /// Creates a new entity with the given components and adds it to the buffer.
    /// </summary>
    public void AddEntity<T1, T2, T3, T4>(T1 c1, T2 c2, T3 c3, T4 c4) where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull
        => Add(new CreateEntityWithComponentsCommand(
            new InitialComponent<T1>(c1),
            new InitialComponent<T2>(c2),
            new InitialComponent<T3>(c3),
            new InitialComponent<T4>(c4)));

    /// <summary>
    /// Creates a new entity with the given components and adds it to the buffer.
    /// </summary>
    public void AddEntity<T1, T2, T3, T4, T5>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5) where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull where T5 : notnull
        => Add(new CreateEntityWithComponentsCommand(
            new InitialComponent<T1>(c1),
            new InitialComponent<T2>(c2),
            new InitialComponent<T3>(c3),
            new InitialComponent<T4>(c4),
            new InitialComponent<T5>(c5)));

    /// <summary>
    /// Creates a new entity with the given components and adds it to the buffer.
    /// </summary>
    public void AddEntity<T1, T2, T3, T4, T5, T6>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6) where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull where T5 : notnull where T6 : notnull
        => Add(new CreateEntityWithComponentsCommand(
            new InitialComponent<T1>(c1),
            new InitialComponent<T2>(c2),
            new InitialComponent<T3>(c3),
            new InitialComponent<T4>(c4),
            new InitialComponent<T5>(c5),
            new InitialComponent<T6>(c6)));

    /// <summary>
    /// Creates a new entity with the given components (array form for dynamic scenarios).
    /// </summary>
    public void AddEntity(IReadOnlyList<IInitialComponent> components)
        => Add(new CreateEntityWithComponentsCommand(components.ToArray()));

    public IEnumerable<ICommand> Commands => _commands;

    public void Apply(EntityManager em)
    {
        var processor = new CommandProcessor(em);
        processor.Process(_commands);
    }

    public void Clear()
    {
        _commands.Clear();
    }
}

/// <summary>
/// Wraps a component value as an IInitialComponent for entity creation.
/// </summary>
public readonly record struct InitialComponent<T>(T Component) : IInitialComponent where T : notnull
{
    public void Apply(EntityManager em, Entity entity)
    {
        em.AddComponent(entity, Component);
    }
}
