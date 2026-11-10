namespace WithWindows.Core.Scheduling;

public sealed class OutputBudget
{
    public OutputBudget(int limitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
        LimitBytes = limitBytes;
    }

    public int LimitBytes { get; }
    public int WrittenBytes { get; private set; }
    public bool IsExhausted => WrittenBytes >= LimitBytes;

    public int Accept(int availableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableBytes);
        int allowed = Math.Min(availableBytes, LimitBytes - WrittenBytes);
        WrittenBytes += allowed;
        return allowed;
    }
}
