using System.ComponentModel;
using System.Reflection;
using Features.Mcp.Tools;
using ModelContextProtocol.Server;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class ToolMetadataTests
{
    private static readonly Type[] ToolTypes =
        [typeof(DiscoveryTools), typeof(ScheduleTools), typeof(DataTools), typeof(WebhookTools)];

    [Fact]
    public void EveryToolAndModelControlledParameterHasDescription()
    {
        var methods = ToolTypes.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();

        // Update this count whenever an MCP tool is added or removed.
        Assert.Equal(18, methods.Length);
        foreach (var method in methods)
        {
            Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
            foreach (var parameter in method.GetParameters().Where(parameter => parameter.ParameterType != typeof(McpTarget)))
                Assert.NotNull(parameter.GetCustomAttribute<DescriptionAttribute>());
        }
    }
}
