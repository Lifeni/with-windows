using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(8));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ww-scheduled-" + Guid.NewGuid().ToString("N"));

    public ScheduledTaskStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ScheduledTaskStore NewStore()
        => new(Path.Combine(_directory, "tasks.json"));

    [Fact]
    public void Add_保存后重新加载仍保留任务()
    {
        ScheduledTaskStore store = NewStore();

        ScheduledTask added = store.Add(SampleTask("生成日报"));
        ScheduledTaskStore reloaded = NewStore();

        ScheduledTask task = Assert.Single(reloaded.GetAll());
        Assert.Equal(added.Id, task.Id);
        Assert.Equal("生成日报", task.Name);
        Assert.Equal(StoreLoadState.Healthy, reloaded.LoadState);
    }

    [Fact]
    public void Load_主文件损坏时隔离并从备份恢复()
    {
        ScheduledTaskStore store = NewStore();
        store.Add(SampleTask("第一版"));
        store.Update(store.GetAll()[0].Id, task => task.Name = "第二版", Now.AddMinutes(1));
        File.WriteAllText(store.Path, "{ 坏");

        ScheduledTaskStore reloaded = NewStore();

        Assert.Equal("第一版", Assert.Single(reloaded.GetAll()).Name);
        Assert.Equal(StoreLoadState.Recovered, reloaded.LoadState);
        Assert.NotEmpty(Directory.GetFiles(_directory, "tasks.json.corrupt-*"));
        Assert.True(File.Exists(reloaded.BackupPath));
    }

    [Fact]
    public void Load_主文件缺失但备份有效时从备份恢复()
    {
        ScheduledTaskStore store = NewStore();
        ScheduledTask added = store.Add(SampleTask("保留我"));
        store.Update(added.Id, task => task.Name = "更新的版本", Now.AddMinutes(1));
        File.Delete(store.Path);

        ScheduledTaskStore reloaded = NewStore();

        Assert.Equal("保留我", Assert.Single(reloaded.GetAll()).Name);
        Assert.Equal(StoreLoadState.Recovered, reloaded.LoadState);
    }

    [Fact]
    public void Load_主文件和备份都不存在时创建空仓储()
    {
        ScheduledTaskStore store = NewStore();

        Assert.Empty(store.GetAll());
        Assert.Equal(StoreLoadState.Empty, store.LoadState);
    }

    [Fact]
    public void Load_主文件和备份均损坏时只读且不覆盖文件()
    {
        string path = Path.Combine(_directory, "tasks.json");
        string backup = path + ".bak";
        File.WriteAllText(path, "{ 坏");
        File.WriteAllText(backup, "{ 也坏");
        string original = File.ReadAllText(path);

        ScheduledTaskStore store = NewStore();

        Assert.Equal(StoreLoadState.Faulted, store.LoadState);
        Assert.Throws<InvalidOperationException>(() => store.Add(SampleTask("禁止写入")));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal("{ 也坏", File.ReadAllText(backup));
    }

    [Fact]
    public void Load_任务ID越界格式_进入故障态()
    {
        File.WriteAllText(
            Path.Combine(_directory, "tasks.json"),
            """{"version":1,"tasks":[{"id":"..","name":"坏任务","schedule":{},"source":{}}]}""");

        ScheduledTaskStore store = NewStore();

        Assert.Equal(StoreLoadState.Faulted, store.LoadState);
    }

    [Fact]
    public void Load_任务名称为null_进入故障态()
    {
        File.WriteAllText(
            Path.Combine(_directory, "tasks.json"),
            """{"version":1,"tasks":[{"id":"d35ec245bf9c4cf1888649d29c84a966","name":null,"schedule":{},"source":{}}]}""");

        ScheduledTaskStore store = NewStore();

        Assert.Equal(StoreLoadState.Faulted, store.LoadState);
    }

    [Fact]
    public void ClaimOccurrence_写入修订号和运行ID并消费一次性任务()
    {
        ScheduledTaskStore store = NewStore();
        ScheduledTask task = SampleTask("只运行一次");
        task.Schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Now,
        };
        task = store.Add(task);

        ScheduledTask claimed = store.ClaimOccurrence(
            task.Id,
            new ScheduledTaskOccurrence(task.Id, task.ScheduleRevision, Now),
            "run-1",
            Now.AddMilliseconds(20));

        Assert.Equal(Now, claimed.LastHandledScheduledFor);
        Assert.Equal("run-1", claimed.LastHandledRunId);
        Assert.Equal(Now.AddMilliseconds(20), claimed.Schedule.OneTimeConsumedAt);
        Assert.Equal("run-1", Assert.Single(NewStore().GetAll()).LastHandledRunId);
    }

    [Fact]
    public void UpdatePlan_递增计划修订号并清除旧认领()
    {
        ScheduledTaskStore store = NewStore();
        ScheduledTask task = store.Add(SampleTask("日报"));
        store.ClaimOccurrence(
            task.Id,
            new ScheduledTaskOccurrence(task.Id, task.ScheduleRevision, Now),
            "run-1",
            Now.AddMilliseconds(20));

        ScheduledTask updated = store.UpdatePlan(
            task.Id,
            schedule =>
            {
                schedule.Kind = ScheduleKind.Daily;
                schedule.TimeOfDay = new TimeOnly(10, 30);
            },
            Now.AddMinutes(1));

        Assert.Equal(2, updated.ScheduleRevision);
        Assert.Null(updated.LastHandledScheduledFor);
        Assert.Null(updated.LastHandledRunId);
    }

    [Fact]
    public void UpdatePlan_一次性任务改期后清除旧消费标记()
    {
        ScheduledTaskStore store = NewStore();
        ScheduledTask task = SampleTask("只运行一次");
        task.Schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Now,
        };
        task = store.Add(task);
        store.ClaimOccurrence(
            task.Id,
            new ScheduledTaskOccurrence(task.Id, task.ScheduleRevision, Now),
            "run-1",
            Now.AddMilliseconds(20));

        DateTimeOffset nextRunAt = Now.AddMinutes(5);
        ScheduledTask updated = store.UpdatePlan(
            task.Id,
            schedule => schedule.OneTimeRunAt = nextRunAt,
            Now.AddMinutes(1));

        Assert.Equal(2, updated.ScheduleRevision);
        Assert.Null(updated.Schedule.OneTimeConsumedAt);
        Assert.Equal(
            nextRunAt,
            ScheduleCalculator.NextAfter(
                updated.Schedule,
                Now.AddMinutes(1),
                TimeZoneInfo.Utc));
    }

    private static ScheduledTask SampleTask(string name) => new()
    {
        Name = name,
        Enabled = false,
        Schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 30),
        },
        Source = new ScriptSource
        {
            Kind = ScriptSourceKind.Inline,
            ScriptText = "print('hello')",
        },
        PythonPath = @"C:\Python313\python.exe",
        PythonSource = PythonInterpreterSource.Manual,
        PythonResolvedPath = @"C:\Python313\python.exe",
        WorkingDirectory = @"C:\Users\Example",
        TimeoutSeconds = 60,
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
