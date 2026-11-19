using System.Text.Json;
using WithWindows.Core.Samples;
using WithWindows.Core.Scheduling;
using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public sealed class SampleContentSeederTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 17, 8, 30, 0, TimeSpan.FromHours(8));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ww-samples-" + Guid.NewGuid().ToString("N"));

    public SampleContentSeederTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string TodoPath => Path.Combine(_directory, "todos.json");
    private string TasksPath => Path.Combine(_directory, "scheduled-tasks", "tasks.json");
    private string StatePath => Path.Combine(_directory, "sample-content.json");

    private SampleContentSeedResult Seed()
        => SampleContentSeeder.EnsureInitialContent(
            TodoPath,
            TasksPath,
            StatePath,
            Now);

    [Fact]
    public void EnsureInitialContent_空目录_写入待办脚本和初始化标记()
    {
        SampleContentSeedResult result = Seed();

        Assert.False(result.AlreadyInitialized);
        Assert.Equal(3, result.TodosAdded);
        Assert.Equal(3, result.TasksAdded);
        Assert.True(File.Exists(StatePath));
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.Equal(1, state.RootElement.GetProperty("version").GetInt32());

        TodoStore todos = new(TodoPath);
        Assert.Equal(
            SampleContent.CreateTodoTitles(),
            todos.GetAll().Select(item => item.Title));

        ScheduledTaskStore tasks = new(TasksPath);
        Assert.Collection(
            tasks.GetAll(),
            task =>
            {
                Assert.Equal("示例：手动环境巡检", task.Name);
                Assert.Equal(ScheduleKind.Manual, task.Schedule.Kind);
                Assert.Contains("Environment.MachineName", task.ScriptText);
            },
            task =>
            {
                Assert.Equal("示例：每日摘要", task.Name);
                Assert.Equal(ScheduleKind.Daily, task.Schedule.Kind);
                Assert.Contains("每日摘要", task.ScriptText);
            },
            task =>
            {
                Assert.Equal("示例：每周发布检查", task.Name);
                Assert.Equal(ScheduleKind.Weekly, task.Schedule.Kind);
                Assert.Contains(DayOfWeek.Friday, task.Schedule.DaysOfWeek);
                Assert.Contains("发布前检查", task.ScriptText);
            });
    }

    [Fact]
    public void EnsureInitialContent_已有标记_不重复写入()
    {
        SampleContentSeedResult first = Seed();
        SampleContentSeedResult second = Seed();

        Assert.False(first.AlreadyInitialized);
        Assert.True(second.AlreadyInitialized);
        Assert.Equal(0, second.TodosAdded);
        Assert.Equal(0, second.TasksAdded);
        Assert.Equal(3, new TodoStore(TodoPath).GetAll().Count);
        Assert.Equal(3, new ScheduledTaskStore(TasksPath).GetAll().Count);
    }

    [Fact]
    public void EnsureInitialContent_用户删空列表后_不会重新写入示例()
    {
        Seed();
        TodoStore todos = new(TodoPath);
        foreach (TodoItem item in todos.GetAll())
            todos.Delete(item.Id);
        ScheduledTaskStore tasks = new(TasksPath);
        foreach (ScheduledTask task in tasks.GetAll())
            tasks.Delete(task.Id);

        SampleContentSeedResult result = Seed();

        Assert.True(result.AlreadyInitialized);
        Assert.Empty(todos.GetAll());
        Assert.Empty(tasks.GetAll());
    }

    [Fact]
    public void EnsureInitialContent_已有待办_保留用户内容并补齐示例脚本()
    {
        var todos = new TodoStore(TodoPath);
        todos.Add("用户自己的待办", Now.AddMinutes(-5));

        SampleContentSeedResult result = Seed();

        Assert.Equal(0, result.TodosAdded);
        Assert.Equal(3, result.TasksAdded);
        Assert.Equal(
            "用户自己的待办",
            Assert.Single(new TodoStore(TodoPath).GetAll()).Title);
        Assert.Equal(3, new ScheduledTaskStore(TasksPath).GetAll().Count);
    }

    [Fact]
    public void EnsureInitialContent_已有待办和脚本_只写初始化标记()
    {
        var todos = new TodoStore(TodoPath);
        todos.Add("用户待办", Now);
        var tasks = new ScheduledTaskStore(TasksPath);
        tasks.Add(new ScheduledTask
        {
            Name = "用户脚本",
            Schedule = new TaskSchedule { Kind = ScheduleKind.Manual },
            ScriptText = "Console.WriteLine(\"用户脚本\");",
            CreatedAt = Now,
        });

        SampleContentSeedResult result = Seed();

        Assert.Equal(0, result.TodosAdded);
        Assert.Equal(0, result.TasksAdded);
        Assert.True(File.Exists(StatePath));
        Assert.Equal("用户待办", Assert.Single(todos.GetAll()).Title);
        Assert.Equal("用户脚本", Assert.Single(tasks.GetAll()).Name);
    }

    [Fact]
    public void EnsureInitialContent_已有损坏数据_不覆盖用户文件()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TasksPath)!);
        File.WriteAllText(TasksPath, "{ 坏");
        File.WriteAllText(TasksPath + ".bak", "{ 也坏");

        SampleContentSeedResult result = Seed();

        Assert.Equal(0, result.TasksAdded);
        Assert.True(File.Exists(StatePath));
        Assert.Equal("{ 坏", File.ReadAllText(TasksPath));
        Assert.Equal("{ 也坏", File.ReadAllText(TasksPath + ".bak"));
    }

    [Fact]
    public void EnsureInitialContent_脚本仅有备份_不覆盖备份()
    {
        var tasks = new ScheduledTaskStore(TasksPath);
        tasks.Add(CreateUserTask("备份中的脚本"));
        tasks.Add(CreateUserTask("用于生成备份的脚本"));
        File.Delete(TasksPath);
        string backupBefore = File.ReadAllText(TasksPath + ".bak");

        SampleContentSeedResult result = Seed();

        Assert.Equal(0, result.TasksAdded);
        Assert.Equal(3, result.TodosAdded);
        Assert.False(File.Exists(TasksPath));
        Assert.Equal(backupBefore, File.ReadAllText(TasksPath + ".bak"));
        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void EnsureInitialContent_标记写入失败后_下次启动补齐标记且不重复()
    {
        Directory.CreateDirectory(StatePath);

        Assert.Throws<IOException>(() => Seed());
        Assert.Equal(3, new TodoStore(TodoPath).GetAll().Count);
        Assert.Equal(3, new ScheduledTaskStore(TasksPath).GetAll().Count);
        Assert.True(Directory.Exists(StatePath));

        Directory.Delete(StatePath);
        SampleContentSeedResult retry = Seed();

        Assert.True(File.Exists(StatePath));
        Assert.Equal(0, retry.TodosAdded);
        Assert.Equal(0, retry.TasksAdded);
        Assert.Equal(3, new TodoStore(TodoPath).GetAll().Count);
        Assert.Equal(3, new ScheduledTaskStore(TasksPath).GetAll().Count);
    }

    [Fact]
    public void EnsureInitialContent_脚本写入失败后_下次启动补齐且不重复待办()
    {
        string tasksDirectory = Path.GetDirectoryName(TasksPath)!;
        File.WriteAllText(tasksDirectory, "占位");

        Assert.Throws<IOException>(() => Seed());
        Assert.Equal(3, new TodoStore(TodoPath).GetAll().Count);
        Assert.False(File.Exists(StatePath));

        File.Delete(tasksDirectory);
        SampleContentSeedResult retry = Seed();

        Assert.Equal(0, retry.TodosAdded);
        Assert.Equal(3, retry.TasksAdded);
        Assert.True(File.Exists(StatePath));
        Assert.Equal(3, new TodoStore(TodoPath).GetAll().Count);
        Assert.Equal(3, new ScheduledTaskStore(TasksPath).GetAll().Count);
    }

    private static ScheduledTask CreateUserTask(string name) => new()
    {
        Name = name,
        Schedule = new TaskSchedule { Kind = ScheduleKind.Manual },
        ScriptText = "Console.WriteLine(\"用户脚本\");",
        CreatedAt = Now,
    };
}
