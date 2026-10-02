import { type AdminApiClient } from '../adminApi/client';
import { PermissionDenied } from '../shell/PermissionDenied';
import { Spinner } from '../shell/Spinner';
import { useAgentsAndActivations } from './useAgentsAndActivations';

/**
 * Two stacked sections, each handling its own loading/error/empty state
 * independently. See useAgentsAndActivations.ts for why.
 */
export function AgentsAndActivations({ client }: { client: AdminApiClient | null }) {
  const {
    agents,
    agentsError,
    agentsErrorStatus,
    activations,
    activationsError,
    activationsErrorStatus,
    loading,
  } = useAgentsAndActivations(client);

  return (
    <div className="mx-auto max-w-2xl space-y-8 p-4">
      {loading && <Spinner />}

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Agents</h2>

        {agentsErrorStatus === 403 ? (
          <PermissionDenied message={agentsError ?? undefined} />
        ) : agentsError ? (
          <p className="text-sm text-red-600">{agentsError}</p>
        ) : !loading && agents.length === 0 ? (
          <p className="text-sm text-slate-500">No agents deployed in this tenant yet.</p>
        ) : (
          agents.map((agent) => (
            <div key={agent.id} className="rounded-md border border-slate-200 p-4">
              <p className="font-medium">{agent.name}</p>
              <p className="text-sm text-slate-500">Created by {agent.createdBy}</p>
            </div>
          ))
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Activations</h2>

        {activationsErrorStatus === 403 ? (
          <PermissionDenied message={activationsError ?? undefined} />
        ) : activationsError ? (
          <p className="text-sm text-red-600">{activationsError}</p>
        ) : !loading && activations.length === 0 ? (
          <p className="text-sm text-slate-500">No activations in this tenant yet.</p>
        ) : (
          activations.map((activation) => (
            <div key={activation.id} className="rounded-md border border-slate-200 p-4">
              <div className="mb-1 flex items-center gap-2">
                <span className="font-medium">{activation.name}</span>
                <span
                  className={`rounded-full px-2 py-0.5 text-xs ${
                    activation.active ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-700'
                  }`}
                >
                  {activation.active ? 'active' : 'inactive'}
                </span>
              </div>
              <p className="text-sm text-slate-500">
                {activation.agentName}
                {activation.description ? `: ${activation.description}` : ''}
              </p>
            </div>
          ))
        )}
      </section>
    </div>
  );
}
