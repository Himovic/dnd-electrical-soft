namespace DND.Electrical.Shared;

/// <summary>Abstraction over the system clock so that time-dependent logic is testable.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset Now => DateTimeOffset.Now;
}
