using Shared.Providers;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class RedisConnectionSecurityTests
{
    [Theory]
    [InlineData("redis.example.com:6380,password=secret,ssl=true")]
    [InlineData("cache.redis.cache.windows.net:6380,password=access-key,ssl=True,abortConnect=False")]
    public void GetSecurityGap_ReturnsNull_WhenAuthAndTlsPresent(string connectionString)
    {
        Assert.Null(RedisConnectionSecurity.GetSecurityGap(connectionString));
    }

    [Theory]
    [InlineData("localhost:6379")]
    [InlineData("localhost:6379,ssl=true")]
    [InlineData("localhost:6379,password=secret")]
    public void GetSecurityGap_ReportsMissingAuthOrTls(string connectionString)
    {
        var gap = RedisConnectionSecurity.GetSecurityGap(connectionString);

        Assert.NotNull(gap);
        Assert.False(string.IsNullOrWhiteSpace(gap));
    }

    [Fact]
    public void ValidateOrThrow_ThrowsOutsideDevelopment_WhenInsecure()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RedisConnectionSecurity.ValidateOrThrow(
                "localhost:6379",
                isDevelopment: false,
                allowInsecure: false,
                _ => { }));

        Assert.Contains("AUTH and TLS", ex.Message);
        Assert.Contains("xians:cache:invalidate", ex.Message);
    }

    [Fact]
    public void ValidateOrThrow_WarnsInDevelopment_WhenInsecure()
    {
        string? warning = null;

        RedisConnectionSecurity.ValidateOrThrow(
            "localhost:6379",
            isDevelopment: true,
            allowInsecure: false,
            message => warning = message);

        Assert.NotNull(warning);
        Assert.Contains("AUTH and TLS", warning);
    }

    [Fact]
    public void ValidateOrThrow_WarnsWhenAllowInsecureIsSet()
    {
        string? warning = null;

        RedisConnectionSecurity.ValidateOrThrow(
            "localhost:6379",
            isDevelopment: false,
            allowInsecure: true,
            message => warning = message);

        Assert.NotNull(warning);
    }
}
