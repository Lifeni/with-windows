namespace WithWindows.Core;

/// <summary>按名称分发动作实例，大小写不敏感。</summary>
public sealed class ActionRegistry
{
    private readonly Dictionary<string, IAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAction action) => _actions[action.Name] = action;

    public IAction? Find(string name)
        => _actions.TryGetValue(name, out var action) ? action : null;
}
