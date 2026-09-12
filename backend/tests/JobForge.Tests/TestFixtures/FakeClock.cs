using JobForge.Application.Common;

namespace JobForge.Tests.TestFixtures;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
