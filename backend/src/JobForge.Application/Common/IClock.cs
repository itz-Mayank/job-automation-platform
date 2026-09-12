namespace JobForge.Application.Common;

/// <summary>Thin wrapper over UtcNow so retry/stale-timing logic is deterministically testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
