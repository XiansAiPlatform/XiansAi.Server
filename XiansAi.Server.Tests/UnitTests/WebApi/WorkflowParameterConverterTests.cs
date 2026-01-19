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
    public void ConvertParameters_WithTooManyParameters_ThrowsArgumentException()
    {
        // Arrange
        var stringParams = new[] { "value1", "value2" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "param1", Type = "string", Optional = false }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs));
        
        Assert.Contains("Too many parameters", exception.Message);
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
            new ParameterDefinition { Name = "num", Type = "INT", Optional = false },      // uppercase
            new ParameterDefinition { Name = "flag", Type = "Bool", Optional = false },    // mixed case
            new ParameterDefinition { Name = "value", Type = "FLOAT", Optional = false }   // uppercase
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

    [Fact]
    public void ConvertParameters_WithOptionalParameter_MissingOptional_Succeeds()
    {
        // Arrange
        var stringParams = new[] { "required-value" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false },
            new ParameterDefinition { Name = "optional", Type = "string", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Single(result);
        Assert.Equal("required-value", result[0]);
    }

    [Fact]
    public void ConvertParameters_WithAllOptionalParameters_NoParams_Succeeds()
    {
        // Arrange
        var stringParams = Array.Empty<string>();
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "optional1", Type = "string", Optional = true },
            new ParameterDefinition { Name = "optional2", Type = "int", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertParameters_MissingRequiredParameter_ThrowsArgumentException()
    {
        // Arrange
        var stringParams = Array.Empty<string>();
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs));
        
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void ConvertParameters_WithMixedOptionalAndRequired_AllProvided_Succeeds()
    {
        // Arrange
        var stringParams = new[] { "value1", "42", "value3" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required1", Type = "string", Optional = false },
            new ParameterDefinition { Name = "optional1", Type = "int", Optional = true },
            new ParameterDefinition { Name = "required2", Type = "string", Optional = false }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal("value1", result[0]);
        Assert.Equal(42, result[1]);
        Assert.Equal("value3", result[2]);
    }

    [Fact]
    public void ConvertParameters_WithEmptyStringForOptionalParameter_ExcludesParameter()
    {
        // Arrange
        var stringParams = new[] { "required-value", "" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false },
            new ParameterDefinition { Name = "optional", Type = "string", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        // Empty optional parameter should be excluded, not passed as null
        Assert.Single(result);
        Assert.Equal("required-value", result[0]);
    }

    [Fact]
    public void ConvertParameters_WithEmptyStringForRequiredParameter_ThrowsArgumentException()
    {
        // Arrange
        var stringParams = new[] { "" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs));
        
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void ConvertParameters_NullParametersWithOnlyOptional_Succeeds()
    {
        // Arrange
        string[]? stringParams = null;
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "optional1", Type = "string", Optional = true },
            new ParameterDefinition { Name = "optional2", Type = "int", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams!, paramDefs);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertParameters_NullParametersWithRequired_ThrowsArgumentException()
    {
        // Arrange
        string[]? stringParams = null;
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false }
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowParameterConverter.ConvertParameters(stringParams!, paramDefs));
        
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void ConvertParameters_MultipleEmptyOptionalParameters_ExcludesAll()
    {
        // Arrange
        var stringParams = new[] { "https://example.com", "", "", "10" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "url", Type = "string", Optional = false },
            new ParameterDefinition { Name = "mode", Type = "string", Optional = true },
            new ParameterDefinition { Name = "format", Type = "string", Optional = true },
            new ParameterDefinition { Name = "timeout", Type = "int", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        // Should only include url and timeout, skipping the two empty optional params
        Assert.Equal(2, result.Length);
        Assert.Equal("https://example.com", result[0]);
        Assert.Equal(10, result[1]);
    }

    [Fact]
    public void ConvertParameters_OnlyRequiredProvided_AllOptionalEmpty_ExcludesOptionals()
    {
        // Arrange
        var stringParams = new[] { "value1", "" };
        var paramDefs = new List<ParameterDefinition>
        {
            new ParameterDefinition { Name = "required", Type = "string", Optional = false },
            new ParameterDefinition { Name = "optional1", Type = "string", Optional = true }
        };

        // Act
        var result = WorkflowParameterConverter.ConvertParameters(stringParams, paramDefs);

        // Assert
        // Should only include the required parameter
        Assert.Single(result);
        Assert.Equal("value1", result[0]);
    }
}

