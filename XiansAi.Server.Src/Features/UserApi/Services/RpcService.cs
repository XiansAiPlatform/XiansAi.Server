using System.Text.Json;
using Shared.Auth;
using Shared.Utils.Temporal;
using Temporalio.Client;
using System.Linq;
using Temporalio.Api.Enums.V1;

namespace Features.UserApi.Services;

public interface IRpcService
{
    Task<object> HandleRpcRequest(string workflowIdentifier, string procedureName, JsonElement args);
}

public class RpcService : IRpcService
{
    private readonly ITenantContext _tenantContext;
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<RpcService> _logger;
    public RpcService(ITenantContext tenantContext, ITemporalClientService clientService, ILogger<RpcService> logger)
    {
        _tenantContext = tenantContext;
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    }

    public async Task<object> HandleRpcRequest(string workflow, string procedureName, JsonElement args)
    {
        try
        {
            Console.WriteLine($"Handling RPC request for workflow {workflow} with procedure {procedureName} and args {args}");

            var workflowIdentifier = new WorkflowIdentifier(workflow, _tenantContext);

            var client = _clientService.GetClient();
            //var workflowHandle = client.GetWorkflowHandle(workflowId);

            var workflowOptions = new NewWorkflowOptions(
                    workflowIdentifier.AgentName,
                    workflowIdentifier.WorkflowType,
                    workflowIdentifier.WorkflowId,
                    _tenantContext);

            var withStartWorkflowOperation = WithStartWorkflowOperation.Create(
                workflowIdentifier.WorkflowType,
                [],
                workflowOptions
            );

            var workflowUpdateWithStartOptions = new WorkflowUpdateWithStartOptions(
                withStartWorkflowOperation
            );

            var rpcContext = new RpcContext {
                ParticipantId = _tenantContext.LoggedInUser,
                WorkflowId = workflowIdentifier.WorkflowId,
                WorkflowType = workflowIdentifier.WorkflowType,
                Agent = workflowIdentifier.AgentName,
                TenantId = _tenantContext.TenantId,
            };

            // Check if args is an array and deserialize accordingly
            object?[] arguments;
            if (args.ValueKind == JsonValueKind.Array)
            {
                // If it's an array, deserialize each element
                arguments = args.EnumerateArray()
                    .Select(element => element.Deserialize<object>())
                    .ToArray();
            }
            else
            {
                // If it's not an array, wrap the single value in an array
                arguments = [args.Deserialize<object>()];
            }

            arguments = arguments.Append(rpcContext).ToArray();


            return await client.ExecuteUpdateWithStartWorkflowAsync<object>(
                procedureName,
                arguments,
                workflowUpdateWithStartOptions
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC request");
            throw;
        }
    }
}