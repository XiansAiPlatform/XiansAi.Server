using Features.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Configuration;
using Shared.Repositories;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacyOptionsTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => $"Privacy:{v.Key}", v => (string?)v.Value))
            .Build();

    [Fact]
    public void Defaults_AreOffWithSevenDayWindow()
    {
        var options = PrivacyOptions.FromConfiguration(Config());

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromDays(7), options.ErasureDelay);
        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.Equal(10, options.MaxAttempts);
        Assert.Null(options.PseudonymKey);
    }

    [Fact]
    public void ErasureDelay_UsesServerDurationFormat()
    {
        var options = PrivacyOptions.FromConfiguration(Config(("ErasureDelay", "2d 12h")));

        Assert.Equal(TimeSpan.FromHours(60), options.ErasureDelay);
    }

    [Fact]
    public void InvalidErasureDelay_WhenEnabled_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PrivacyOptions.FromConfiguration(Config(("Enabled", "true"), ("ErasureDelay", "seven days"))));
    }

    [Fact]
    public void InvalidErasureDelay_WhenDisabled_DoesNotBlockStartup()
    {
        var services = new ServiceCollection().AddPrivacyServices(Config(("ErasureDelay", "seven days")));

        Assert.Contains(services, d => d.ServiceType == typeof(PrivacyOptions));
    }

    [Theory]
    [InlineData("PollIntervalSeconds")]
    [InlineData("MaxAttempts")]
    public void InvalidNumber_WhenDisabled_DoesNotBlockStartup(string setting)
    {
        var services = new ServiceCollection().AddPrivacyServices(Config((setting, "sixty")));

        Assert.Contains(services, d => d.ServiceType == typeof(PrivacyOptions));
    }

    [Theory]
    [InlineData("PollIntervalSeconds")]
    [InlineData("MaxAttempts")]
    public void InvalidNumber_WhenEnabled_Throws(string setting)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PrivacyOptions.FromConfiguration(Config(("Enabled", "true"), (setting, "sixty"))));
    }

    [Fact]
    public void Disabled_DoesNotRequireKey()
    {
        PrivacyOptions.FromConfiguration(Config()).Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Enabled_WithoutValidKey_Throws(string? key)
    {
        var values = key == null
            ? new[] { ("Enabled", "true") }
            : new[] { ("Enabled", "true"), ("PseudonymKey", key) };

        var options = PrivacyOptions.FromConfiguration(Config(values));

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Enabled_WithValidKey_Passes()
    {
        PrivacyOptions.FromConfiguration(Config(("Enabled", "true"), ("PseudonymKey", ValidKey))).Validate();
    }

    [Fact]
    public void AddPrivacyServices_WhenDisabled_RegistersOnlyOptions()
    {
        var services = new ServiceCollection().AddPrivacyServices(Config());

        Assert.Contains(services, d => d.ServiceType == typeof(PrivacyOptions));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPrivacyRequestRepository));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPrivacySubjectResolver));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPseudonymiser));
    }

    [Fact]
    public void AddPrivacyServices_WhenEnabled_RegistersFoundation()
    {
        var services = new ServiceCollection()
            .AddPrivacyServices(Config(("Enabled", "true"), ("PseudonymKey", ValidKey)));

        Assert.Contains(services, d => d.ServiceType == typeof(IPrivacyRequestRepository));
        Assert.Contains(services, d => d.ServiceType == typeof(IPrivacySubjectResolver));
        Assert.Contains(services, d => d.ServiceType == typeof(IPseudonymiser));
    }

    [Fact]
    public void AddPrivacyServices_WhenEnabledWithoutKey_FailsStartup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPrivacyServices(Config(("Enabled", "true"))));
    }
}
