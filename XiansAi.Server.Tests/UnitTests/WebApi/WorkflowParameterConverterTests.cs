using Features.WebApi.Utils;
using Shared.Data.Models;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.WebApi;

public class WorkflowParameterConverterTests
{
    [Fact]
    public void ConvertParameters_WithStringType_ReturnsString()
    {
        // Arrange
        var stringParams = new[] { "https://shifter.no" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "url", Type = "string" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<string>(result[0]);
        Assert.Equal("https://shifter.no", result[0]);
    }

    [Fact]
    public void ConvertParameters_WithIntegerType_ReturnsInt()
    {
        // Arrange
        var stringParams = new[] { "1" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "count", Type = "int" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<int>(result[0]);
        Assert.Equal(1, result[0]);
    }

    [Fact]
    public void ConvertParameters_WithMultipleTypes_ReturnsCorrectTypes()
    {
        // Arrange
        var stringParams = new[] { "https://shifter.no", "1" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "url", Type = "string" },
            new ParameterDefinition { Name = "count", Type = "int" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.IsType<string>(result[0]);
        Assert.Equal("https://shifter.no", result[0]);
        Assert.IsType<int>(result[1]);
        Assert.Equal(1, result[1]);
    }

    [Fact]
    public void ConvertParameters_WithBooleanType_ReturnsBool()
    {
        // Arrange
        var stringParams = new[] { "true", "false" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "enabled", Type = "bool" },
            new ParameterDefinition { Name = "disabled", Type = "boolean" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.IsType<bool>(result[0]);
        Assert.True((bool)result[0]);
        Assert.IsType<bool>(result[1]);
        Assert.False((bool)result[1]);
    }

    [Fact]
    public void ConvertParameters_WithFloatType_ReturnsFloat()
    {
        // Arrange
        var stringParams = new[] { "3.14", "2.5" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "value1", Type = "float" },
            new ParameterDefinition { Name = "value2", Type = "double" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.IsType<float>(result[0]);
        Assert.Equal(3.14f, (float)result[0], 2);
        Assert.IsType<double>(result[1]);
        Assert.Equal(2.5, (double)result[1], 2);
    }

    [Fact]
    public void ConvertParameters_WithLongType_ReturnsLong()
    {
        // Arrange
        var stringParams = new[] { "9223372036854775807" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "bigNumber", Type = "long" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<long>(result[0]);
        Assert.Equal(9223372036854775807L, result[0]);
    }

    [Fact]
    public void ConvertParameters_WithDateTimeType_ReturnsDateTime()
    {
        // Arrange
        var stringParams = new[] { "2024-01-15T10:30:00" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "timestamp", Type = "datetime" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<DateTime>(result[0]);
        var dateTime = (DateTime)result[0];
        Assert.Equal(2024, dateTime.Year);
        Assert.Equal(1, dateTime.Month);
        Assert.Equal(15, dateTime.Day);
    }

    [Fact]
    public void ConvertParameters_WithGuidType_ReturnsGuid()
    {
        // Arrange
        var guidString = "12345678-1234-1234-1234-123456789012";
        var stringParams = new[] { guidString };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "id", Type = "guid" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<Guid>(result[0]);
        Assert.Equal(Guid.Parse(guidString), result[0]);
    }

    [Fact]
    public void ConvertParameters_WithUrlType_ReturnsString()
    {
        // Arrange
        var stringParams = new[] { "https://example.com/api" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "endpoint", Type = "url" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<string>(result[0]);
        Assert.Equal("https://example.com/api", result[0]);
    }

    [Fact]
    public void ConvertParameters_WithInvalidInteger_ThrowsArgumentException()
    {
        // Arrange
        var stringParams = new[] { "not-a-number" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "count", Type = "int" }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs));
        
        Assert.Contains("count", exception.Message);
        Assert.Contains("int", exception.Message);
    }

    [Fact]
    public void ConvertParameters_WithMismatchedParameterCount_ThrowsArgumentException()
    {
        // Arrange
        var stringParams = new[] { "value1", "value2" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "param1", Type = "string" }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs));
        
        Assert.Contains("Parameter count mismatch", exception.Message);
    }

    [Fact]
    public void ConvertParameters_WithEmptyParameters_ReturnsEmptyArray()
    {
        // Arrange
        var stringParams = Array.Empty<string>();
        var paramDefs = new List<ParameterDefinition>();

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertParameters_WithNullParameters_ReturnsEmptyArray()
    {
        // Arrange
        string[]? stringParams = null;
        var paramDefs = new List<ParameterDefinition>();

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams!, paramDefs);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertParameters_WithUnknownType_ReturnsAsString()
    {
        // Arrange
        var stringParams = new[] { "some-value" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "custom", Type = "unknown-type" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<string>(result[0]);
        Assert.Equal("some-value", result[0]);
    }

    [Fact]
    public void ConvertParameters_WithDecimalType_ReturnsDecimal()
    {
        // Arrange
        var stringParams = new[] { "123.456" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "price", Type = "decimal" }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.IsType<decimal>(result[0]);
        Assert.Equal(123.456m, result[0]);
    }

    [Fact]
    public void ConvertParameters_CaseInsensitiveTypes_WorksCorrectly()
    {
        // Arrange
        var stringParams = new[] { "42", "true", "3.14" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "num", Type = "INT" },      // uppercase
            new ParameterDefinition { Name = "flag", Type = "Bool" },    // mixed case
            new ParameterDefinition { Name = "value", Type = "FLOAT" }   // uppercase
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.IsType<int>(result[0]);
        Assert.Equal(42, result[0]);
        Assert.IsType<bool>(result[1]);
        Assert.True((bool)result[1]);
        Assert.IsType<float>(result[2]);
        Assert.Equal(3.14f, (float)result[2], 2);
    }
}

