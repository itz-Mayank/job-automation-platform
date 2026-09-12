using System.Net;
using FluentAssertions;
using JobForge.Application.Common;
using JobForge.Application.Services;
using JobForge.Domain.Exceptions;
using Xunit;

namespace JobForge.Tests.Unit;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata endpoint
    [InlineData("0.0.0.0")]
    public void IsBlockedAddress_BlocksPrivateAndLoopbackRanges(string ip)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    public void IsBlockedAddress_AllowsPublicAddresses(string ip)
    {
        SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeFalse();
    }

    [Fact]
    public void UrlValidator_RejectsLocalhostUrl()
    {
        var validator = new UrlValidator();
        var act = () => validator.ValidateJobUrl("http://localhost:8080/admin");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void UrlValidator_RejectsLiteralPrivateIp()
    {
        var validator = new UrlValidator();
        var act = () => validator.ValidateJobUrl("http://169.254.169.254/latest/meta-data");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void UrlValidator_RejectsNonHttpScheme()
    {
        var validator = new UrlValidator();
        var act = () => validator.ValidateJobUrl("ftp://example.com/file");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void UrlValidator_AllowsPublicHttpsUrl()
    {
        var validator = new UrlValidator();
        var act = () => validator.ValidateJobUrl("https://api.example.com/webhook");
        act.Should().NotThrow();
    }
}
