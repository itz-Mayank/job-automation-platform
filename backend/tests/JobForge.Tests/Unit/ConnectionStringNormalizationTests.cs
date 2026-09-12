using FluentAssertions;
using JobForge.Infrastructure;
using Xunit;

namespace JobForge.Tests.Unit;

public class ConnectionStringNormalizationTests
{
    [Fact]
    public void KeywordFormat_IsPassedThroughUnchanged()
    {
        const string input = "Host=localhost;Port=5432;Database=jobforge;Username=jobforge;Password=jobforge";

        DependencyInjection.NormalizeConnectionString(input).Should().Be(input);
    }

    [Fact]
    public void PostgresUri_IsConvertedToKeywordFormat()
    {
        const string uri = "postgres://myuser:my%40pass@dpg-example.render.com:5432/jobforge_db";

        var result = DependencyInjection.NormalizeConnectionString(uri);

        result.Should().Contain("Host=dpg-example.render.com");
        result.Should().Contain("Port=5432");
        result.Should().Contain("Database=jobforge_db");
        result.Should().Contain("Username=myuser");
        result.Should().Contain("Password=my@pass");
        result.Should().Contain("SSL Mode=Require");
    }
}
