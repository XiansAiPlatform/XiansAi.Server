using System.Text.Json;
using Temporalio.Converters;

namespace Features.WebApi.Services;

public static class ScheduleWorkflowInput
{
    public static IReadOnlyList<JsonElement> Decode(IEnumerable<object?> arguments, IPayloadConverter converter) =>
        arguments.Select(argument => DecodeArgument(argument, converter)).ToArray();

    private static JsonElement DecodeArgument(object? argument, IPayloadConverter converter)
    {
        try
        {
            return argument is IEncodedRawValue encoded
                ? JsonSerializer.SerializeToElement(converter.ToValue(encoded.Payload, typeof(JsonElement?)))
                : JsonSerializer.SerializeToElement(argument);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return JsonSerializer.SerializeToElement(new { unavailable = true, reason = "This input cannot be displayed as JSON." });
        }
    }
}
