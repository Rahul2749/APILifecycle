namespace ApiLifecycle;

/// <summary>An immutable policy. Dates apply automatically without restarting the host.</summary>
public sealed record ApiVersionPolicy
{
    public required string Version { get; init; }
    public LifecycleState State { get; init; } = LifecycleState.Active;
    public DateTimeOffset? DeprecationAt { get; init; }
    public DateTimeOffset? SunsetAt { get; init; }
    public string? SuccessorVersion { get; init; }
    public Uri? SuccessorUrl { get; init; }
    public Uri? PolicyUrl { get; init; }
    public IReadOnlyList<BrownoutWindow> Brownouts { get; init; } = [];

    public LifecycleState GetState(DateTimeOffset now)
    {
        if (State == LifecycleState.Retired || SunsetAt is { } sunset && now >= sunset)
            return LifecycleState.Retired;
        if (State is LifecycleState.Deprecated or LifecycleState.Sunset)
            return State;
        return DeprecationAt is { } date && now >= date ? LifecycleState.Deprecated : State;
    }
}
