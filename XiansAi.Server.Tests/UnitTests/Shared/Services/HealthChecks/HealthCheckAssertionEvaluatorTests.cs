using Shared.Data.Models.HealthChecks;
using Shared.Services.HealthChecks;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

public class HealthCheckAssertionEvaluatorTests
{
    private static HealthCheckAssertion Contains(string value) => new() { Op = HealthCheckAssertionOp.Contains, Value = value };
    private static HealthCheckAssertion NotContains(string value) => new() { Op = HealthCheckAssertionOp.NotContains, Value = value };

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate("Hello, how can I HELP?", [Contains("hello"), Contains("help")]);

        Assert.True(passed);
        Assert.All(results, r => Assert.True(r.Passed));
    }

    [Fact]
    public void NotContains_FailsWhenPresentInAnyCase()
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate("An ERROR occurred", [NotContains("error")]);

        Assert.False(passed);
        Assert.False(Assert.Single(results).Passed);
    }

    [Fact]
    public void AllAssertionsMustPass()
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate("Hi there", [Contains("hi"), Contains("goodbye")]);

        Assert.False(passed);
        Assert.Equal([true, false], results.Select(r => r.Passed));
    }

    [Fact]
    public void NullResponse_IsTreatedAsEmpty()
    {
        var (passed, _) = HealthCheckAssertionEvaluator.Evaluate(null, [Contains("x")]);
        Assert.False(passed);

        var (notContainsPassed, _) = HealthCheckAssertionEvaluator.Evaluate(null, [NotContains("x")]);
        Assert.True(notContainsPassed);
    }

    [Fact]
    public void NoAssertions_Pass()
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate("anything", []);

        Assert.True(passed);
        Assert.Empty(results);
    }

    [Fact]
    public void DescribeFailures_ListsOnlyFailedAssertions()
    {
        var (_, results) = HealthCheckAssertionEvaluator.Evaluate("Hi", [Contains("hi"), Contains("bye"), NotContains("hi")]);

        var description = HealthCheckAssertionEvaluator.DescribeFailures(results);

        Assert.Equal("Assertion failed: expected response to contain 'bye'; expected response not to contain 'hi'", description);
    }
}
