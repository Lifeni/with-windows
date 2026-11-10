using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class OutputBudgetTests
{
    [Fact]
    public void Accept_只返回尚未达到上限的字节数()
    {
        var budget = new OutputBudget(4);

        Assert.Equal(3, budget.Accept(3));
        Assert.Equal(1, budget.Accept(3));
        Assert.Equal(0, budget.Accept(1));
        Assert.Equal(4, budget.WrittenBytes);
        Assert.True(budget.IsExhausted);
    }

    [Fact]
    public void Accept_非法上限被拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputBudget(0));
    }
}
