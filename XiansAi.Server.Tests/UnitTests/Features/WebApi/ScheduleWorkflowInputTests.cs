using System.Text.Json;
using Features.WebApi.Services;
using Temporalio.Converters;

namespace XiansAi.Server.Tests.UnitTests.Features.WebApi;

public class ScheduleWorkflowInputTests
{
    private readonly DefaultPayloadConverter _converter = new();

    [Fact]
    public void PreservesPromptAndParametersFromTemporalPayload()
    {
        var value = new { Prompt = "Summarize React issues", Parameters = new { count = "5" } };
        var argument = new EncodedRawValue(DataConverter.Default, _converter.ToPayload(value));
        var input = ScheduleWorkflowInput.Decode([argument], _converter);
        Assert.Equal(value.Prompt, input[0].GetProperty("Prompt").GetString());
        Assert.Equal("5", input[0].GetProperty("Parameters").GetProperty("count").GetString());
    }

    [Fact]
    public void PreservesArgumentOrderAndEmptyInput()
    {
        var input = ScheduleWorkflowInput.Decode(["first", 2, JsonSerializer.SerializeToElement(new { nested = true })], _converter);
        Assert.Equal("first", input[0].GetString());
        Assert.Equal(2, input[1].GetInt32());
        Assert.True(input[2].GetProperty("nested").GetBoolean());
        Assert.Empty(ScheduleWorkflowInput.Decode([], _converter));
    }

    [Fact]
    public void PreservesNullPayload()
    {
        var argument = new EncodedRawValue(DataConverter.Default, _converter.ToPayload(null));
        var input = ScheduleWorkflowInput.Decode([argument], _converter);
        Assert.Equal(JsonValueKind.Null, input[0].ValueKind);
    }

    [Fact]
    public void UnsupportedPayloadDoesNotBreakScheduleDetails()
    {
        var argument = new EncodedRawValue(DataConverter.Default, _converter.ToPayload(new byte[] { 1, 2 }));
        var input = ScheduleWorkflowInput.Decode([argument, "second"], _converter);
        Assert.True(input[0].GetProperty("unavailable").GetBoolean());
        Assert.Equal("second", input[1].GetString());
    }
}
