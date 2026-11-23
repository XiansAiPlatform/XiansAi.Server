using Shared.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Guards the plugin failure email: it must name what failed and what started it, keep the
/// subject on one line, and keep the end of long errors.
/// </summary>
public class PluginFailureEmailTests
{
    private static PluginFailureNotification Notification(string? error = "boom") => new()
    {
        AgentName = "Xianix AI-DLC Agent",
        Plugins = ["pr-reviewer"],
        Repository = "acme/app",
        Trigger = "PR #42 (webhook 'pull requests')",
        ExecutionId = "abc123",
        Error = error,
        OccurredAt = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void BuildSubject_NamesPluginAndRepository_OnOneLine()
    {
        var subject = PluginFailureEmail.BuildSubject(Notification() with { Repository = "acme/app\r\nBcc: x@evil.com" });

        Assert.StartsWith("[Xians] Plugin failed: pr-reviewer (acme/app", subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
    }

    [Fact]
    public void BuildSubject_NoPlugins_SaysAPluginRunFailed()
    {
        Assert.Equal("[Xians] A plugin run failed", PluginFailureEmail.BuildSubject(new PluginFailureNotification()));
    }

    [Fact]
    public void BuildBody_IncludesContextAndKeepsTheTailOfLongErrors()
    {
        var error = new string('x', PluginFailureEmail.MaxErrorLength + 100) + "root cause";

        var body = PluginFailureEmail.BuildBody("acme", Notification(error));

        Assert.Contains("Tenant: acme", body);
        Assert.Contains("Plugin(s): pr-reviewer", body);
        Assert.Contains("Trigger: PR #42 (webhook 'pull requests')", body);
        Assert.Contains("Execution ID: abc123", body);
        Assert.Contains("Time (UTC): 2026-11-19 10:00:00", body);
        Assert.Contains("root cause", body);
        Assert.DoesNotContain(new string('x', PluginFailureEmail.MaxErrorLength + 1), body);
    }

    [Fact]
    public void BuildBody_NoError_SaysSo()
    {
        Assert.Contains("No error details were reported.", PluginFailureEmail.BuildBody("acme", Notification(error: null)));
    }
}
