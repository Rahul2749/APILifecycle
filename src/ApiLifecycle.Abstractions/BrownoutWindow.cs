namespace ApiLifecycle;

/// <summary>A one-off UTC interval, including its start and excluding its end.</summary>
public sealed record BrownoutWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;
}
