using System.Globalization;
using Shared.Data.Models;

namespace Features.WebApi.Utils;

/// <summary>
/// Utility class for converting workflow parameters from strings to their proper types
/// based on workflow definition parameter definitions.
/// </summary>
public static class WorkflowParameterConverter
{
    /// <summary>
    /// Converts an array of string parameters to their proper types based on the workflow definition.
    /// </summary>
    /// <param name="stringParameters">The string parameters to convert.</param>
    /// <param name="parameterDefinitions">The parameter definitions from the workflow definition.</param>
    /// <returns>An array of objects with properly typed parameters.</returns>
    /// <exception cref="ArgumentException">Thrown when parameter count mismatch or conversion fails.</exception>
    public static object[] ConvertParameters(string[] stringParameters, List<ParameterDefinition> parameterDefinitions)
    {
        if (stringParameters == null || stringParameters.Length == 0)
        {
            return Array.Empty<object>();
        }

        if (parameterDefinitions == null || parameterDefinitions.Count == 0)
        {
            // If no parameter definitions exist, return strings as-is
            return stringParameters;
        }

        if (stringParameters.Length != parameterDefinitions.Count)
        {
            throw new ArgumentException(
                $"Parameter count mismatch. Expected {parameterDefinitions.Count} parameters but got {stringParameters.Length}");
        }

        var convertedParameters = new object[stringParameters.Length];

        for (int i = 0; i < stringParameters.Length; i++)
        {
            var stringValue = stringParameters[i];
            var parameterDefinition = parameterDefinitions[i];
            
            try
            {
                convertedParameters[i] = ConvertParameter(stringValue, parameterDefinition.Type, parameterDefinition.Name);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Failed to convert parameter '{parameterDefinition.Name}' at index {i} to type '{parameterDefinition.Type}'. Value: '{stringValue}'", 
                    ex);
            }
        }

        return convertedParameters;
    }

    /// <summary>
    /// Converts a single parameter from string to its proper type.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <param name="targetType">The target type as a string.</param>
    /// <param name="parameterName">The parameter name for error messages.</param>
    /// <returns>The converted value as an object.</returns>
    private static object ConvertParameter(string value, string targetType, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Parameter '{parameterName}' cannot be null or empty");
        }

        // Normalize the type name (case-insensitive, handle common variations)
        var normalizedType = targetType.ToLowerInvariant().Trim();

        return normalizedType switch
        {
            // String types
            "string" or "str" or "text" => value,
            
            // Integer types
            "int" or "integer" or "int32" => 
                int.TryParse(value, out var intValue) 
                    ? intValue 
                    : throw new FormatException($"Cannot convert '{value}' to integer"),
            
            "long" or "int64" => 
                long.TryParse(value, out var longValue) 
                    ? longValue 
                    : throw new FormatException($"Cannot convert '{value}' to long"),
            
            "short" or "int16" => 
                short.TryParse(value, out var shortValue) 
                    ? shortValue 
                    : throw new FormatException($"Cannot convert '{value}' to short"),
            
            // Floating point types
            "float" or "single" => 
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue) 
                    ? floatValue 
                    : throw new FormatException($"Cannot convert '{value}' to float"),
            
            "double" => 
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) 
                    ? doubleValue 
                    : throw new FormatException($"Cannot convert '{value}' to double"),
            
            "decimal" => 
                decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue) 
                    ? decimalValue 
                    : throw new FormatException($"Cannot convert '{value}' to decimal"),
            
            // Boolean type
            "bool" or "boolean" => 
                bool.TryParse(value, out var boolValue) 
                    ? boolValue 
                    : throw new FormatException($"Cannot convert '{value}' to boolean"),
            
            // DateTime types
            "datetime" or "date" => 
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue) 
                    ? dateValue 
                    : throw new FormatException($"Cannot convert '{value}' to datetime"),
            
            "datetimeoffset" => 
                DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOffsetValue) 
                    ? dateOffsetValue 
                    : throw new FormatException($"Cannot convert '{value}' to datetimeoffset"),
            
            // Guid type
            "guid" or "uuid" => 
                Guid.TryParse(value, out var guidValue) 
                    ? guidValue 
                    : throw new FormatException($"Cannot convert '{value}' to GUID"),
            
            // Byte type
            "byte" => 
                byte.TryParse(value, out var byteValue) 
                    ? byteValue 
                    : throw new FormatException($"Cannot convert '{value}' to byte"),
            
            // Char type
            "char" or "character" => 
                value.Length == 1 
                    ? value[0] 
                    : throw new FormatException($"Cannot convert '{value}' to char (must be single character)"),
            
            // URL type
            "url" or "uri" => 
                Uri.TryCreate(value, UriKind.Absolute, out var uriValue) 
                    ? value // Return as string for URL, since that's what most workflows expect
                    : throw new FormatException($"Cannot convert '{value}' to valid URL"),
            
            // Default case: if type is not recognized, return as string
            _ => value
        };
    }
}

