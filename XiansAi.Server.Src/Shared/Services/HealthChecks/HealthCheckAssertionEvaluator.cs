using Shared.Data.Models.HealthChecks;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Evaluates the deterministic substring assertions shared by chat and webhook checks (R5):
/// case-insensitive "contains" / "does not contain". A response passes only when every assertion does.
/// </summary>
public static class HealthCheckAssertionEvaluator
{
    public static (bool AllPassed, List<HealthCheckAssertionResult> Results) Evaluate(
        string? response, IEnumerable<HealthCheckAssertion> assertions)
    {
        var text = response ?? string.Empty;
        var results = assertions
            .Select(assertion =>
            {
                var contains = text.Contains(assertion.Value, StringComparison.OrdinalIgnoreCase);
                return new HealthCheckAssertionResult
                {
                    Op = assertion.Op,
                    Value = assertion.Value,
                    Passed = assertion.Op == HealthCheckAssertionOp.Contains ? contains : !contains
                };
            })
            .ToList();

        return (results.All(r => r.Passed), results);
    }

    /// <summary>A one-line summary of the assertions that failed, for the run detail.</summary>
    public static string DescribeFailures(IEnumerable<HealthCheckAssertionResult> results)
    {
        var failures = results
            .Where(r => !r.Passed)
            .Select(r => r.Op == HealthCheckAssertionOp.Contains
                ? $"expected response to contain '{r.Value}'"
                : $"expected response not to contain '{r.Value}'");

        return "Assertion failed: " + string.Join("; ", failures);
    }
}
