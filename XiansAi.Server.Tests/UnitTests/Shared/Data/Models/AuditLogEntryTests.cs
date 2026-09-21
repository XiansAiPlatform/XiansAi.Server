using Shared.Data.Models;
using Xunit;

namespace Tests.UnitTests.Shared.Data.Models;

public class AuditLogEntryTests
{
    [Fact]
    public void SanitizeAndReturn_SanitizesDetailKeysAndStringValues()
    {
        var entry = CreateEntry(new Dictionary<string, object?>
        {
            ["  Agent Name "] = "  demo\u0001agent  ",
            ["Count"] = 7,
            ["\x01"] = "ignored-empty-key"
        });

        var sanitized = entry.SanitizeAndReturn();

        Assert.Equal("demoagent", sanitized.Details!["Agent Name"]);
        Assert.Equal(7, sanitized.Details["Count"]);
        Assert.False(sanitized.Details.ContainsKey(""));
        Assert.False(sanitized.Details.ContainsKey("\x01"));
    }

    [Fact]
    public void SanitizeAndReturn_SanitizesNestedDetailDictionaries()
    {
        var entry = CreateEntry(new Dictionary<string, object?>
        {
            ["Nested"] = new Dictionary<string, object?>
            {
                ["  Inner "] = " value\u0007 "
            }
        });

        var sanitized = entry.SanitizeAndReturn();
        var nested = Assert.IsType<Dictionary<string, object?>>(sanitized.Details!["Nested"]);

        Assert.Equal("value", nested["Inner"]);
    }

    private static AuditLogEntry CreateEntry(Dictionary<string, object?> details) => new()
    {
        TenantId = "test-tenant",
        ParticipantId = "participant-1",
        LoggedInUser = "user-1",
        Action = "tenant.created",
        Details = details
    };
}
