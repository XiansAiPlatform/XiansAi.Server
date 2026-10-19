using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class AdminStatsServiceUserStatsTests
{
    private const string TenantId = "tenant-1";
    private static readonly DateTime From = new(2026, 12, 1);
    private static readonly DateTime To = new(2026, 12, 9);

    private readonly Mock<IAdminTaskService> _tasks = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly AdminStatsService _service;

    public AdminStatsServiceUserStatsTests()
    {
        _service = new AdminStatsService(
            _tasks.Object,
            _conversations.Object,
            _activations.Object,
            _users.Object,
            NullLogger<AdminStatsService>.Instance);
    }

    [Fact]
    public async Task Groups_Agents_And_Tasks_By_Activation_Creator()
    {
        SetupActivations(
            Activation("agent-a", "act-1", "user-1", new DateTime(2026, 12, 2)),
            Activation("agent-a", "act-2", "user-1", new DateTime(2026, 12, 5)),
            Activation("agent-b", "act-3", "user-2", new DateTime(2026, 11, 1))); // created before range
        SetupTaskCounts(new()
        {
            [("agent-a", "act-1")] = 10,
            [("agent-a", "act-2")] = 5,
            [("agent-b", "act-3")] = 7
        });
        _users.Setup(r => r.GetByUserIdAsync("user-1")).ReturnsAsync(new User { UserId = "user-1", Name = "Erik Eide" });
        _users.Setup(r => r.GetByUserIdAsync("user-2")).ReturnsAsync(new User { UserId = "user-2", Name = "Ann Lee" });

        var result = await _service.GetUserStatsAsync(TenantId, From, To);

        Assert.True(result.IsSuccess);
        var data = result.Data!;
        Assert.Equal("2026-12-01", data.From);
        Assert.Equal("2026-12-09", data.To);
        Assert.Equal(2, data.Totals.Users);
        Assert.Equal(2, data.Totals.Agents);
        Assert.Equal(22, data.Totals.Tasks);

        Assert.Collection(data.Users,
            u =>
            {
                Assert.Equal("user-1", u.UserId);
                Assert.Equal("Erik Eide", u.DisplayName);
                Assert.Equal(2, u.AgentCount);
                Assert.Equal(15, u.TasksCreated);
            },
            u =>
            {
                Assert.Equal("user-2", u.UserId);
                Assert.Equal("Ann Lee", u.DisplayName);
                Assert.Equal(0, u.AgentCount);
                Assert.Equal(7, u.TasksCreated);
            });
    }

    [Fact]
    public async Task Tasks_From_Unknown_Activation_Go_To_Unknown_User()
    {
        SetupActivations();
        SetupTaskCounts(new() { [("agent-x", "deleted")] = 3 });

        var result = await _service.GetUserStatsAsync(TenantId, From, To);

        var user = Assert.Single(result.Data!.Users);
        Assert.Equal(AdminStatsService.UnknownUserId, user.UserId);
        Assert.Equal("Unknown", user.DisplayName);
        Assert.Equal(3, user.TasksCreated);
    }

    [Fact]
    public async Task Task_Count_Failure_Returns_InternalServerError()
    {
        _tasks.Setup(t => t.GetTaskCountsByActivation(TenantId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(ServiceResult<Dictionary<(string AgentName, string ActivationName), int>>.InternalServerError("boom"));

        var result = await _service.GetUserStatsAsync(TenantId, From, To);

        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task Missing_User_Record_Falls_Back_To_UserId()
    {
        SetupActivations(Activation("agent-a", "act-1", "ghost", new DateTime(2026, 12, 3)));
        SetupTaskCounts(new());

        var result = await _service.GetUserStatsAsync(TenantId, From, To);

        var user = Assert.Single(result.Data!.Users);
        Assert.Equal("ghost", user.DisplayName);
    }

    [Fact]
    public async Task End_Date_Is_Inclusive()
    {
        SetupActivations(Activation("agent-a", "act-1", "user-1", new DateTime(2026, 12, 9, 23, 59, 0)));
        SetupTaskCounts(new());

        var result = await _service.GetUserStatsAsync(TenantId, From, To);

        Assert.Equal(1, result.Data!.Totals.Agents);
        _tasks.Verify(t => t.GetTaskCountsByActivation(TenantId, From, new DateTime(2026, 12, 10)), Times.Once);
    }

    [Fact]
    public async Task Missing_Dates_Return_BadRequest()
    {
        var result = await _service.GetUserStatsAsync(TenantId, null, To);

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Start_After_End_Returns_BadRequest()
    {
        var result = await _service.GetUserStatsAsync(TenantId, To, From);

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }

    private void SetupActivations(params AgentActivation[] activations) =>
        _activations.Setup(r => r.GetByTenantIdAsync(TenantId)).ReturnsAsync(activations.ToList());

    private void SetupTaskCounts(Dictionary<(string AgentName, string ActivationName), int> counts) =>
        _tasks.Setup(t => t.GetTaskCountsByActivation(TenantId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(ServiceResult<Dictionary<(string AgentName, string ActivationName), int>>.Success(counts));

    private static AgentActivation Activation(string agent, string name, string createdBy, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..24],
        Name = name,
        AgentName = agent,
        CreatedBy = createdBy,
        CreatedAt = createdAt,
        TenantId = TenantId
    };
}
