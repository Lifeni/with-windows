using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void SignalActivation_第二个实例_能唤醒等待中的第一个实例()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string mutexName = $@"Local\WithWindows.Test.{suffix}";
        string eventName = $@"Local\WithWindows.Test.Activate.{suffix}";

        using var owner = new SingleInstance(mutexName, eventName);
        Assert.True(owner.Owned);

        // 必须在另一个线程构造：同一线程对同一 Mutex 的 WaitOne(0) 会递归成功，测不出「已有实例」
        SingleInstance? second = null;
        var thread = new Thread(() =>
        {
            second = new SingleInstance(mutexName, eventName);
            second.SignalActivation();
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        Assert.NotNull(second);
        Assert.False(second!.Owned);
        Assert.True(owner.WaitForActivation(millisecondsTimeout: 2000));

        second.Dispose();
    }

    [Fact]
    public void WaitForActivation_没有信号_超时返回假()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var owner = new SingleInstance(
            $@"Local\WithWindows.Test.{suffix}",
            $@"Local\WithWindows.Test.Activate.{suffix}");

        Assert.False(owner.WaitForActivation(millisecondsTimeout: 50));
    }

    [Fact]
    public void WakeActivationWaiter_能唤醒等待中的线程()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var owner = new SingleInstance(
            $@"Local\WithWindows.Test.{suffix}",
            $@"Local\WithWindows.Test.Activate.{suffix}");

        owner.WakeActivationWaiter();

        Assert.True(owner.WaitForActivation(millisecondsTimeout: 1000));
    }
}
