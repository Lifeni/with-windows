using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ww-todo-" + Guid.NewGuid().ToString("N"));

    public TodoStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TodoStore NewStore() => new(Path.Combine(_dir, "todos.json"));

    [Fact]
    public void Add_保存后重新加载仍保留标题()
    {
        TodoStore store = NewStore();

        TodoItem added = store.Add("买牛奶", DateTimeOffset.UnixEpoch);
        TodoStore reloaded = NewStore();

        TodoItem item = Assert.Single(reloaded.GetAll());
        Assert.Equal(added.Id, item.Id);
        Assert.Equal("买牛奶", item.Title);
    }

    [Fact]
    public void SetCompleted_完成时清除提醒()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("提交周报", DateTimeOffset.UnixEpoch);
        store.SetReminder(
            item.Id,
            DateTimeOffset.UnixEpoch.AddHours(1),
            DateTimeOffset.UnixEpoch);

        store.SetCompleted(
            item.Id,
            true,
            DateTimeOffset.UnixEpoch.AddMinutes(1));

        TodoItem updated = Assert.Single(store.GetAll());
        Assert.True(updated.IsCompleted);
        Assert.Null(updated.ReminderAt);
    }

    [Fact]
    public void Delete_移除待办并持久化()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("临时事项", DateTimeOffset.UnixEpoch);

        Assert.True(store.Delete(item.Id));
        Assert.Empty(NewStore().GetAll());
    }

    [Fact]
    public void Load_主文件损坏时从备份恢复()
    {
        TodoStore store = NewStore();
        store.Add("保留我", DateTimeOffset.UnixEpoch);
        store.Add("后一项", DateTimeOffset.UnixEpoch.AddMinutes(1));
        File.WriteAllText(store.Path, "{ 坏");

        TodoItem item = Assert.Single(NewStore().GetAll());
        Assert.Equal("保留我", item.Title);
    }

    [Fact]
    public void Load_主文件缺失时从备份恢复()
    {
        TodoStore store = NewStore();
        store.Add("保留我", DateTimeOffset.UnixEpoch);
        store.Add("后一项", DateTimeOffset.UnixEpoch.AddMinutes(1));
        File.Delete(store.Path);

        TodoItem item = Assert.Single(NewStore().GetAll());
        Assert.Equal("保留我", item.Title);
    }

    [Fact]
    public void Load_主文件含null项目时从备份恢复()
    {
        TodoStore store = NewStore();
        store.Add("保留我", DateTimeOffset.UnixEpoch);
        store.Add("后一项", DateTimeOffset.UnixEpoch.AddMinutes(1));
        File.WriteAllText(store.Path, """{"version":1,"items":[null]}""");

        TodoItem item = Assert.Single(NewStore().GetAll());
        Assert.Equal("保留我", item.Title);
    }

    [Fact]
    public void MarkReminderDelivered_不再进入到期列表()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("提醒", DateTimeOffset.UnixEpoch);
        store.SetReminder(
            item.Id,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            DateTimeOffset.UnixEpoch);

        store.MarkReminderDelivered(
            item.Id,
            DateTimeOffset.UnixEpoch.AddMinutes(1));

        Assert.Empty(TodoReminderPolicy.GetDue(
            store.GetAll(),
            DateTimeOffset.UnixEpoch.AddMinutes(2)));
    }
}
