namespace Spacevors.Domain;

public interface ICommand { }

public readonly record struct CreateEntityWithComponentsCommand : ICommand
{
    public int AssignedId { get; init; } = -1;
    public object[] Components { get; init; } = Array.Empty<object>();

    public CreateEntityWithComponentsCommand(params object[] components)
    {
        Components = components;
    }
}

public readonly record struct AddComponentCommand<T> : ICommand where T : notnull
{
    public Entity Entity { get; }
    public T Component { get; }

    public AddComponentCommand(Entity entity, T component)
    {
        Entity = entity;
        Component = component;
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
