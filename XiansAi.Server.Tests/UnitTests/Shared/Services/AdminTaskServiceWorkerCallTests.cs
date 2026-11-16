using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Exceptions;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// A missing task worker is reported as a timeout. A real error from a worker that answered
/// late must not be rewritten as "worker is not available".
/// </summary>
public class AdminTaskServiceWorkerCallTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task CallWorkerAsync_WhenTheCallNeverCompletes_ReportsTheWorkerUnavailable()
    {
        var exception = await Assert.ThrowsAsync<AdminTaskService.WorkerUnavailableException>(() =>
            AdminTaskService.CallWorkerAsync<int>(
                "wf-1",
                async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return 0;
                },
                ShortTimeout));

        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task CallWorkerAsync_WhenTemporalReportsNoPoller_ReportsTheWorkerUnavailable()
    {
        var rpc = new RpcException(
            RpcException.StatusCode.FailedPrecondition,
            "no poller seen for task queue recently, worker may be down",
            null!);

        var exception = await Assert.ThrowsAsync<AdminTaskService.WorkerUnavailableException>(() =>
            AdminTaskService.CallWorkerAsync<int>("wf-1", _ => throw rpc, ShortTimeout));

        Assert.Same(rpc, exception.InnerException);
    }

    [Fact]
    public async Task CallWorkerAsync_WhenThePollerMessageIsWrapped_ReportsTheWorkerUnavailable()
    {
        var rpc = new RpcException(
            RpcException.StatusCode.FailedPrecondition,
            "worker may be down",
            null!);
        var wrapped = new InvalidOperationException("query failed", rpc);

        var exception = await Assert.ThrowsAsync<AdminTaskService.WorkerUnavailableException>(() =>
            AdminTaskService.CallWorkerAsync<int>("wf-1", _ => throw wrapped, ShortTimeout));

        Assert.Same(wrapped, exception.InnerException);
    }

    [Fact]
    public async Task CallWorkerAsync_WhenARealErrorArrivesAfterTheTimeout_KeepsThatError()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminTaskService.CallWorkerAsync<int>(
                "wf-1",
                async token =>
                {
                    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    await cancelled.Task;
                    throw new InvalidOperationException("query rejected");
                },
                ShortTimeout));

        Assert.Equal("query rejected", exception.Message);
    }

    [Fact]
    public async Task CallWorkerAsync_WhenTheRpcIsCancelledByOurTimeout_ReportsTheWorkerUnavailable()
    {
        var exception = await Assert.ThrowsAsync<AdminTaskService.WorkerUnavailableException>(() =>
            AdminTaskService.CallWorkerAsync<int>(
                "wf-1",
                async token =>
                {
                    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    await cancelled.Task;
                    throw new RpcException(RpcException.StatusCode.Cancelled, "operation was canceled", null!);
                },
                ShortTimeout));

        var rpc = Assert.IsType<RpcException>(exception.InnerException);
        Assert.Equal(RpcException.StatusCode.Cancelled, rpc.Code);
    }

    [Fact]
    public async Task CallWorkerAsync_WhenTheRpcIsCancelledBeforeOurTimeout_KeepsThatError()
    {
        var rpc = new RpcException(RpcException.StatusCode.Cancelled, "operation was canceled", null!);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            AdminTaskService.CallWorkerAsync<int>("wf-1", _ => throw rpc, ShortTimeout));

        Assert.Same(rpc, exception);
    }

    [Fact]
    public void WorkerUnavailable_ReturnsRequestTimeoutWithTheWorkerMessage()
    {
        var service = new AdminTaskService(
            Mock.Of<ITemporalGatewayFactory>(),
            NullLogger<AdminTaskService>.Instance);
        var cause = new InvalidOperationException("no poller seen for task queue recently");

        var result = service.WorkerUnavailable<object>(
            "wf-1",
            new AdminTaskService.WorkerUnavailableException("wf-1", cause));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.RequestTimeout, result.StatusCode);
        Assert.Equal(AdminTaskService.WorkerUnavailableMessage, result.ErrorMessage);
    }
}
