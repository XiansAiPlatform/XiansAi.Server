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
        var (passed, _) = HealthCheckAssertionEvaluator.Evaluate((string?)null, [Contains("x")]);
        Assert.False(passed);

        var (notContainsPassed, _) = HealthCheckAssertionEvaluator.Evaluate((string?)null, [NotContains("x")]);
        Assert.True(notContainsPassed);
    }

    // ----- JSON and header assertions (webhook responses) -----

    private static HealthCheckAssertion JsonEquals(string path, string value) => new() { Op = HealthCheckAssertionOp.JsonEquals, Path = path, Value = value };
    private static HealthCheckAssertion JsonExists(string path) => new() { Op = HealthCheckAssertionOp.JsonExists, Path = path };
    private static HealthCheckAssertion HeaderEquals(string name, string value) => new() { Op = HealthCheckAssertionOp.HeaderEquals, Path = name, Value = value };
    private static HealthCheckAssertion HeaderExists(string name) => new() { Op = HealthCheckAssertionOp.HeaderExists, Path = name };

    private const string Body = "{\"status\":\"ok\",\"data\":{\"count\":3,\"ready\":true,\"items\":[{\"name\":\"first\"},{\"name\":\"second\"}]}}";

    [Theory]
    [InlineData("$.status", "ok", true)]
    [InlineData("$.status", "OK", false)]             // string equality is exact
    [InlineData("$.data.count", "3", true)]
    [InlineData("$.data.count", "3.00", true)]        // numbers compare by value
    [InlineData("$.data.ready", "true", true)]
    [InlineData("$.data.items[1].name", "second", true)]
    [InlineData("$.data.items[5].name", "second", false)]
    [InlineData("$.missing", "x", false)]
    public void JsonEquals_ReadsTheValueAtThePath(string path, string expected, bool passes)
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate(new HealthCheckResponse(Body), [JsonEquals(path, expected)]);

        Assert.Equal(passes, passed);
        Assert.Equal(path, Assert.Single(results).Path);
    }

    [Fact]
    public void JsonEquals_ReportsTheActualValueOnFailure()
    {
        var (_, results) = HealthCheckAssertionEvaluator.Evaluate(new HealthCheckResponse(Body), [JsonEquals("$.status", "degraded")]);

        Assert.Equal("ok", results[0].Actual);
        Assert.Equal("Assertion failed: expected $.status to equal 'degraded' but got ok", HealthCheckAssertionEvaluator.DescribeFailures(results));
    }

    [Fact]
    public void JsonExists_PassesForAnyValueIncludingObjects()
    {
        var (passed, _) = HealthCheckAssertionEvaluator.Evaluate(new HealthCheckResponse(Body), [JsonExists("$.data"), JsonExists("$.data.items[0]")]);
        Assert.True(passed);

        var (missing, _) = HealthCheckAssertionEvaluator.Evaluate(new HealthCheckResponse(Body), [JsonExists("$.data.owner")]);
        Assert.False(missing);
    }

    [Fact]
    public void JsonAssertions_FailOnNonJsonBodies()
    {
        var (passed, results) = HealthCheckAssertionEvaluator.Evaluate(new HealthCheckResponse("<html>oops</html>"), [JsonExists("$.status")]);

        Assert.False(passed);
        Assert.Equal("response is not JSON", results[0].Actual);
    }

    [Fact]
    public void HeaderAssertions_MatchNamesAndValuesCaseInsensitively()
    {
        var headers = new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"], ["X-Request-Id"] = ["r-1"] };
        var response = new HealthCheckResponse("{}", headers);

        var (passed, _) = HealthCheckAssertionEvaluator.Evaluate(response, [HeaderEquals("content-type", "Application/JSON"), HeaderExists("x-request-id")]);
        Assert.True(passed);

        var (absent, results) = HealthCheckAssertionEvaluator.Evaluate(response, [HeaderExists("X-Missing")]);
        Assert.False(absent);
        Assert.Contains("expected header X-Missing to be present", HealthCheckAssertionEvaluator.DescribeFailures(results));
    }

    [Theory]
    [InlineData("$", true)]
    [InlineData("$.a.b", true)]
    [InlineData("$.items[0].name", true)]
    [InlineData("a.b", false)]
    [InlineData("$..a", false)]
    [InlineData("$.items[*]", false)]
    [InlineData("$.items[0", false)]
    public void JsonPath_AcceptsOnlyTheSupportedSubset(string path, bool valid)
    {
        Assert.Equal(valid, JsonPath.TryParse(path, out _));
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
