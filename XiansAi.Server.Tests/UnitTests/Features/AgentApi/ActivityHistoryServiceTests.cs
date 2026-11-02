using Features.AgentApi.Repositories;
using Features.AgentApi.Services.Lib;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Xunit;

namespace Tests.UnitTests.Features.AgentApi;

public class ActivityHistoryServiceTests
{
    private readonly Mock<IActivityHistoryRepository> _repository = new();
    private ActivityHistory? _saved;

    public ActivityHistoryServiceTests()
    {
        _repository
            .Setup(r => r.CreateWithoutWaiting(It.IsAny<ActivityHistory>()))
            .Callback<ActivityHistory>(activity => _saved = activity);
    }

    private ActivityHistoryService CreateService() =>
        new(_repository.Object, NullLogger<ActivityHistoryService>.Instance);

    private static ActivityHistoryRequest Request(DateTime? startedTime) => new()
    {
        ActivityId = "activity-1",
        ActivityName = "Activity",
        StartedTime = startedTime,
        WorkflowId = "workflow-1",
        WorkflowRunId = "run-1",
        WorkflowType = "Type",
        TaskQueue = "queue",
        WorkflowNamespace = "default"
    };

    [Fact]
    public void Create_KeepsProvidedStartedTime()
    {
        var startedTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        CreateService().Create(Request(startedTime));

        Assert.Equal(startedTime, _saved?.StartedTime);
    }

    [Fact]
    public void Create_DefaultsMissingStartedTimeToNow()
    {
        var before = DateTime.UtcNow;

        CreateService().Create(Request(null));

        Assert.NotNull(_saved?.StartedTime);
        Assert.InRange(_saved!.StartedTime!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public void Create_ReplacesFutureStartedTimeWithNow()
    {
        var before = DateTime.UtcNow;

        CreateService().Create(Request(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.NotNull(_saved?.StartedTime);
        Assert.InRange(_saved!.StartedTime!.Value, before, DateTime.UtcNow);
    }
}
