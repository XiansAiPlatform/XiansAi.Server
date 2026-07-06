using Features.WebApi.Utils;
using Shared.Data.Models;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.WebApi;

public class WorkflowParameterConverterTests
{
    [Theory]
    [InlineData("hello", "string", "hello")]
    [InlineData("42", "int", 42)]
    [InlineData("9223372036854775807", "long", 9223372036854775807L)]
    [InlineData("true", "bool", true)]
    [InlineData("false", "boolean", false)]
    [InlineData("1.5", "float", 1.5f)]
    [InlineData("2.5", "double", 2.5)]
    [InlineData("42", "INT", 42)]      // type matching is case-insensitive
    [InlineData("true", "Bool", true)]
    public void ConvertParameters_ConvertsSupportedTypes(string input, string type, object expected)
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "p", Type = type }
        };

        var result = WorkflowParameterConverter.ConvertParameters(new[] { input }, paramDefs);

        Assert.Single(result);
        Assert.Equal(expected.GetType(), result[0].GetType());
        Assert.Equal(expected, result[0]);
    }

    [Theory]
    [InlineData("url")]        // url is treated as a plain string
    [InlineData("unknown-type")] // unknown types fall back to string
    public void ConvertParameters_TreatsUrlAndUnknownTypesAsString(string type)
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "p", Type = type }
        };

        var result = WorkflowParameterConverter.ConvertParameters(new[] { "https://example.com" }, paramDefs);

        Assert.Single(result);
        Assert.IsType<string>(result[0]);
        Assert.Equal("https://example.com", result[0]);
    }

    [Fact]
    public void ConvertParameters_ConvertsGuidAndDateTime()
    {
        var guid = "12345678-1234-1234-1234-123456789012";
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "id", Type = "guid" },
            new ParameterDefinition { Name = "when", Type = "datetime" }
        };

        var result = WorkflowParameterConverter.ConvertParameters(new[] { guid, "2024-01-15T10:30:00" }, paramDefs);

        Assert.Equal(Guid.Parse(guid), Assert.IsType<Guid>(result[0]));
        var dateTime = Assert.IsType<DateTime>(result[1]);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), dateTime);
    }

    [Fact]
    public void ConvertParameters_WithInvalidConversion_ThrowsWithParameterAndType()
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "count", Type = "int" }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(new[] { "not-a-number" }, paramDefs));

        Assert.Contains("count", exception.Message);
        Assert.Contains("int", exception.Message);
    }

    [Fact]
    public void ConvertParameters_WithTooManyParameters_Throws()
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "param1", Type = "string", Optional = false }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(new[] { "value1", "value2" }, paramDefs));

        Assert.Contains("Too many parameters", exception.Message);
    }

    [Fact]
    public void ConvertParameters_MissingRequiredParameter_Throws()
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(Array.Empty<string>(), paramDefs));

        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void ConvertParameters_ExcludesEmptyOptionalParameters()
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "url", Type = "string", Optional = false },
            new ParameterDefinition { Name = "mode", Type = "string", Optional = true },
            new ParameterDefinition { Name = "timeout", Type = "int", Optional = true }
        };

        var result = WorkflowParameterConverter.ConvertParameters(new[] { "https://example.com", "", "10" }, paramDefs);

        Assert.Equal(2, result.Length);
        Assert.Equal("https://example.com", result[0]);
        Assert.Equal(10, result[1]);
    }

    [Fact]
    public void ConvertParameters_WithNullOrEmptyInput_ReturnsEmptyWhenAllOptional()
    {
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "optional", Type = "string", Optional = true }
        };

        Assert.Empty(WorkflowParameterConverter.ConvertParameters(null!, paramDefs));
        Assert.Empty(WorkflowParameterConverter.ConvertParameters(Array.Empty<string>(), paramDefs));
    }
}
