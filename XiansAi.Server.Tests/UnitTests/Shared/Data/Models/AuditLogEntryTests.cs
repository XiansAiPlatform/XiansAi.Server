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

    [Fact]
    public void SanitizeAndReturn_CapsDetailEntriesAndStripsMarkup()
    {
        var details = new Dictionary<string, object?>
        {
            ["markup"] = "<script>alert('x')</script>",
            ["long"] = new string('a', AuditLogEntry.MaxDetailStringLength + 25),
            [new string('k', AuditLogEntry.MaxDetailKeyLength + 1)] = "skipped-long-key"
        };
        for (var i = 0; i < AuditLogEntry.MaxDetailEntries + 5; i++)
        {
            details[$"key-{i}"] = i;
        }

        var sanitized = CreateEntry(details).SanitizeAndReturn();

        Assert.True(sanitized.Details!.Count <= AuditLogEntry.MaxDetailEntries);
        Assert.Equal("scriptalert(x)/script", sanitized.Details["markup"]);
        Assert.Equal(AuditLogEntry.MaxDetailStringLength, ((string)sanitized.Details["long"]!).Length);
        Assert.False(sanitized.Details.Keys.Any(key => key.Length > AuditLogEntry.MaxDetailKeyLength));
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
