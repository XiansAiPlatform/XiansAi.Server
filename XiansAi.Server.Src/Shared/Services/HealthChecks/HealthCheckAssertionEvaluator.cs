using System.Globalization;
using System.Text.Json;
using Shared.Data.Models.HealthChecks;

namespace Shared.Services.HealthChecks;

/// <summary>What a check received: the response body and, for webhooks, its headers.</summary>
public sealed record HealthCheckResponse(string? Text, IReadOnlyDictionary<string, string[]>? Headers = null);

/// <summary>
/// Evaluates health-check assertions. Text ops (R5) are case-insensitive substring checks on the whole
/// body. JSON ops read one value with a small JSON path (<c>$.data.items[0].status</c>); equality is
/// exact for strings and numeric for numbers. Header ops look the header up case-insensitively and
/// compare its value case-insensitively. A response passes only when every assertion does.
/// </summary>
public static class HealthCheckAssertionEvaluator
{
    private const int MaxActualLength = 200;

    public static (bool AllPassed, List<HealthCheckAssertionResult> Results) Evaluate(
        string? response, IEnumerable<HealthCheckAssertion> assertions)
        => Evaluate(new HealthCheckResponse(response), assertions);

    public static (bool AllPassed, List<HealthCheckAssertionResult> Results) Evaluate(
        HealthCheckResponse response, IEnumerable<HealthCheckAssertion> assertions)
    {
        var text = response.Text ?? string.Empty;
        JsonDocument? json = null;
        var jsonParsed = false;

        try
        {
            var results = assertions.Select(assertion =>
            {
                var (passed, actual) = assertion.Op switch
                {
                    HealthCheckAssertionOp.Contains => (text.Contains(assertion.Value, StringComparison.OrdinalIgnoreCase), (string?)null),
                    HealthCheckAssertionOp.NotContains => (!text.Contains(assertion.Value, StringComparison.OrdinalIgnoreCase), null),
                    HealthCheckAssertionOp.JsonEquals or HealthCheckAssertionOp.JsonExists => EvaluateJson(assertion, Json()),
                    HealthCheckAssertionOp.HeaderEquals or HealthCheckAssertionOp.HeaderExists => EvaluateHeader(assertion, response.Headers),
                    _ => (false, "unsupported assertion")
                };

                return new HealthCheckAssertionResult
                {
                    Op = assertion.Op,
                    Path = assertion.Path,
                    Value = assertion.Value,
                    Passed = passed,
                    Actual = Truncate(actual)
                };
            }).ToList();

            return (results.All(r => r.Passed), results);
        }
        finally
        {
            json?.Dispose();
        }

        JsonDocument? Json()
        {
            if (!jsonParsed)
            {
                jsonParsed = true;
                try { json = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text); }
                catch (JsonException) { json = null; }
            }
            return json;
        }
    }

    /// <summary>A one-line summary of the assertions that failed, for the run detail.</summary>
    public static string DescribeFailures(IEnumerable<HealthCheckAssertionResult> results)
    {
        var failures = results.Where(r => !r.Passed).Select(r => r.Op switch
        {
            HealthCheckAssertionOp.Contains => $"expected response to contain '{r.Value}'",
            HealthCheckAssertionOp.NotContains => $"expected response not to contain '{r.Value}'",
            HealthCheckAssertionOp.JsonEquals => $"expected {r.Path} to equal '{r.Value}' but got {r.Actual ?? "nothing"}",
            HealthCheckAssertionOp.JsonExists => $"expected {r.Path} to exist{(r.Actual != null ? $" ({r.Actual})" : string.Empty)}",
            HealthCheckAssertionOp.HeaderEquals => $"expected header {r.Path} to equal '{r.Value}' but got {r.Actual ?? "nothing"}",
            HealthCheckAssertionOp.HeaderExists => $"expected header {r.Path} to be present",
            _ => $"unsupported assertion {r.Op}"
        });

        return "Assertion failed: " + string.Join("; ", failures);
    }

    private static (bool Passed, string? Actual) EvaluateJson(HealthCheckAssertion assertion, JsonDocument? document)
    {
        if (document == null)
        {
            return (false, "response is not JSON");
        }

        if (!JsonPath.TryParse(assertion.Path, out var segments) || !JsonPath.TrySelect(document.RootElement, segments, out var element))
        {
            return (false, assertion.Op == HealthCheckAssertionOp.JsonExists ? null : "nothing at that path");
        }

        if (assertion.Op == HealthCheckAssertionOp.JsonExists)
        {
            return (true, ValueText(element));
        }

        var actual = ValueText(element);
        var expected = assertion.Value.Trim();
        var equal = element.ValueKind == JsonValueKind.Number
            && decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
            && element.TryGetDecimal(out var actualNumber)
                ? expectedNumber == actualNumber
                : string.Equals(actual, expected, StringComparison.Ordinal);

        return (equal, actual);
    }

    private static (bool Passed, string? Actual) EvaluateHeader(HealthCheckAssertion assertion, IReadOnlyDictionary<string, string[]>? headers)
    {
        var values = headers?
            .FirstOrDefault(h => string.Equals(h.Key, assertion.Path, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (values == null || values.Length == 0)
        {
            return (false, null);
        }

        var actual = string.Join(", ", values);
        return assertion.Op == HealthCheckAssertionOp.HeaderExists
            ? (true, actual)
            : (string.Equals(actual.Trim(), assertion.Value.Trim(), StringComparison.OrdinalIgnoreCase), actual);
    }

    /// <summary>Strings without quotes; numbers, booleans and null as written; objects and arrays as JSON.</summary>
    private static string ValueText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        _ => element.GetRawText()
    };

    private static string? Truncate(string? value) =>
        value == null || value.Length <= MaxActualLength ? value : value[..MaxActualLength] + "…";
}

/// <summary>
/// The JSON path subset health checks accept: <c>$</c>, then <c>.name</c> and <c>[index]</c> segments,
/// e.g. <c>$.data.items[0].status</c>. No wildcards, filters or recursive descent.
/// </summary>
public static class JsonPath
{
    public static bool TryParse(string? path, out List<object> segments)
    {
        segments = new List<object>();
        if (string.IsNullOrWhiteSpace(path) || path[0] != '$')
        {
            return false;
        }

        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var start = ++i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i == start) return false;
                segments.Add(path[start..i]);
            }
            else if (path[i] == '[')
            {
                var close = path.IndexOf(']', i);
                if (close < 0 || !int.TryParse(path[(i + 1)..close], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    return false;
                }
                segments.Add(index);
                i = close + 1;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static bool TrySelect(JsonElement root, IEnumerable<object> segments, out JsonElement element)
    {
        element = root;
        foreach (var segment in segments)
        {
            if (segment is string name)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return false;
            }
            else if (segment is int index)
            {
                if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength()) return false;
                element = element[index];
            }
        }

        return true;
    }
}
