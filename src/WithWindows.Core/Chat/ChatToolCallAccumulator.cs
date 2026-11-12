using System.Text;

namespace WithWindows.Core.Chat;

/// <summary>按索引拼接流式工具调用的名称和 JSON 参数。</summary>
public sealed class ChatToolCallAccumulator
{
    private readonly Dictionary<int, Builder> _builders = [];

    public void Add(ChatToolCallDelta delta)
    {
        if (!_builders.TryGetValue(delta.Index, out Builder? builder))
        {
            builder = new Builder();
            _builders[delta.Index] = builder;
        }

        builder.Id.Append(delta.Id);
        builder.Name.Append(delta.Name);
        builder.Arguments.Append(delta.ArgumentsDelta);
    }

    public IReadOnlyList<ChatToolCall> Build()
        => _builders
            .OrderBy(pair => pair.Key)
            .Select(pair => new ChatToolCall(
                pair.Value.Id.ToString(),
                pair.Value.Name.ToString(),
                pair.Value.Arguments.ToString()))
            .ToList();

    private sealed class Builder
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }
}
