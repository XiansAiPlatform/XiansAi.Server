using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Utils;

namespace Tests.UnitTests.Shared.Utils;

public class ConfigDurationTests
{
    private const string Key = "Some:Duration";

    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value == null ? [] : new Dictionary<string, string?> { [Key] = value })
            .Build();

    private static void VerifyWarnings(Mock<ILogger> logger, Times times) =>
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    [Fact]
    public void Get_MissingSetting_ReturnsNullWithoutWarning()
    {
        var logger = new Mock<ILogger>();

        Assert.Null(ConfigDuration.Get(Config(null), Key, logger.Object));
        VerifyWarnings(logger, Times.Never());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_BlankSetting_ReturnsNullWithoutWarning(string value)
    {
        var logger = new Mock<ILogger>();

        Assert.Null(ConfigDuration.Get(Config(value), Key, logger.Object));
        VerifyWarnings(logger, Times.Never());
    }

    [Fact]
    public void Get_ValidSetting_ReturnsDuration()
    {
        var logger = new Mock<ILogger>();

        Assert.Equal(TimeSpan.FromDays(1) + TimeSpan.FromHours(6), ConfigDuration.Get(Config("1d 6h"), Key, logger.Object));
        VerifyWarnings(logger, Times.Never());
    }

    [Fact]
    public void Get_InvalidSetting_ReturnsNullAndWarnsOnce()
    {
        var logger = new Mock<ILogger>();

        Assert.Null(ConfigDuration.Get(Config("thirty days"), Key, logger.Object));
        VerifyWarnings(logger, Times.Once());
    }
}
