using System.Text.Json;
using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class RunHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(8));
    private const string TaskId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherTaskId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string MissingTaskId = "cccccccccccccccccccccccccccccccc";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ww-runs-" + Guid.NewGuid().ToString("N"));

    public RunHistoryStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private RunHistoryStore NewStore()
        => new(Path.Combine(_directory, "runs.json"));

    [Fact]
    public void Upsert_保存后重新加载仍保留记录()
    {
        RunHistoryStore store = NewStore();
        ScheduledTaskRun run = Run("run-1", 0);

        store.Upsert(run);
        RunHistoryStore reloaded = NewStore();

        ScheduledTaskRun saved = Assert.Single(reloaded.GetAll());
        Assert.Equal(run.Id, saved.Id);
        Assert.Equal(ScheduledTaskRunStatus.Running, saved.Status);
    }

    [Fact]
    public void Upsert_同一ID更新而不新增()
    {
        RunHistoryStore store = NewStore();
        ScheduledTaskRun run = Run("run-1", 0);
        store.Upsert(run);

        run.Status = ScheduledTaskRunStatus.Succeeded;
        run.ExitCode = 0;
        store.Upsert(run);

        Assert.Equal(ScheduledTaskRunStatus.Succeeded, Assert.Single(store.GetAll()).Status);
    }

    [Fact]
    public void Upsert_每任务只保留最近一百条并返回裁剪日志()
    {
        RunHistoryStore store = NewStore();
        var pruned = new List<string>();
        for (int index = 0; index < 105; index++)
            pruned.AddRange(store.Upsert(Run($"new-{index}", index)));

        IReadOnlyList<ScheduledTaskRun> runs = store.GetAll(TaskId);
        Assert.Equal(100, runs.Count);
        Assert.Contains(runs, run => run.Id == "new-104");
        Assert.DoesNotContain(runs, run => run.Id == "new-0");
        Assert.Contains(
            Path.Combine(_directory, "logs", TaskId, "old.stdout.log"),
            pruned);
    }

    [Fact]
    public void RepairInterruptedRuns_把运行中记录改为中断()
    {
        RunHistoryStore store = NewStore();
        store.Upsert(Run("run-1", 0));

        int repaired = store.RepairInterruptedRuns(Now.AddMinutes(5));

        ScheduledTaskRun run = Assert.Single(store.GetAll());
        Assert.Equal(1, repaired);
        Assert.Equal(ScheduledTaskRunStatus.Interrupted, run.Status);
        Assert.Equal(Now.AddMinutes(5), run.FinishedAt);
        Assert.Equal("应用上次未正常结束。", run.ErrorMessage);
    }

    [Fact]
    public void RemoveTask_删除该任务全部历史和日志引用()
    {
        RunHistoryStore store = NewStore();
        store.Upsert(Run("run-1", 0));
        store.Upsert(Run("run-2", 1));
        ScheduledTaskRun other = Run("run-3", 2);
        other.TaskId = OtherTaskId;
        other.StdoutLogPath = Path.Combine(
            _directory,
            "logs",
            OtherTaskId,
            "run-3.stdout.log");
        other.StderrLogPath = Path.Combine(
            _directory,
            "logs",
            OtherTaskId,
            "run-3.stderr.log");
        store.Upsert(other);

        IReadOnlyList<string> removedLogs = store.RemoveTask(TaskId);

        Assert.Single(store.GetAll(), run => run.TaskId == OtherTaskId);
        Assert.Equal(4, removedLogs.Count);
    }

    [Fact]
    public void RemoveTasksNotIn_清理无任务引用的历史()
    {
        RunHistoryStore store = NewStore();
        store.Upsert(Run("keep", 0));
        ScheduledTaskRun orphan = Run("orphan", 1);
        orphan.TaskId = MissingTaskId;
        orphan.StdoutLogPath = Path.Combine(
            _directory,
            "logs",
            MissingTaskId,
            "orphan.stdout.log");
        orphan.StderrLogPath = Path.Combine(
            _directory,
            "logs",
            MissingTaskId,
            "orphan.stderr.log");
        store.Upsert(orphan);

        IReadOnlyList<string> removedLogs =
            store.RemoveTasksNotIn(new HashSet<string>([TaskId]));

        Assert.Single(store.GetAll());
        Assert.Equal(2, removedLogs.Count);
    }

    [Fact]
    public void Load_主文件损坏时从备份恢复()
    {
        RunHistoryStore store = NewStore();
        store.Upsert(Run("old", 0));
        store.Upsert(Run("new", 1));
        File.WriteAllText(store.Path, "{ 坏");

        RunHistoryStore reloaded = NewStore();

        Assert.Contains(reloaded.GetAll(), run => run.Id == "old");
        Assert.Equal(StoreLoadState.Recovered, reloaded.LoadState);
    }

    [Fact]
    public void Load_主文件和备份都损坏时进入故障态()
    {
        string path = Path.Combine(_directory, "runs.json");
        File.WriteAllText(path, "{ 坏");
        File.WriteAllText(path + ".bak", "{ 也坏");

        RunHistoryStore store = NewStore();

        Assert.Equal(StoreLoadState.Faulted, store.LoadState);
        Assert.False(store.CanWrite);
    }

    [Fact]
    public void Load_日志路径越出任务目录_进入故障态()
    {
        var run = Run("run-1", 0);
        run.StdoutLogPath = Path.Combine(_directory, "outside.log");
        string json = JsonSerializer.Serialize(new
        {
            version = 1,
            runs = new[] { run },
        });
        File.WriteAllText(Path.Combine(_directory, "runs.json"), json);

        RunHistoryStore store = NewStore();

        Assert.Equal(StoreLoadState.Faulted, store.LoadState);
    }

    private ScheduledTaskRun Run(string id, int index) => new()
    {
        Id = id,
        TaskId = TaskId,
        TaskName = "任务",
        Trigger = ScheduledTaskRunTrigger.Manual,
        StartedAt = Now.AddSeconds(index),
        Status = ScheduledTaskRunStatus.Running,
        RuntimeVersion = "C# 14 / .NET 10",
        StdoutLogPath = Path.Combine(
            _directory,
            "logs",
            TaskId,
            index == 0 ? "old.stdout.log" : $"{id}.stdout.log"),
        StderrLogPath = Path.Combine(
            _directory,
            "logs",
            TaskId,
            $"{id}.stderr.log"),
    };
}
