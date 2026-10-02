import { type AdminApiClient } from '../adminApi/client';
import { PermissionDenied } from '../shell/PermissionDenied';
import { Spinner } from '../shell/Spinner';
import { useTasks } from './useTasks';

/**
 * One list of tasks, with the actions each one currently allows. See
 * docs/optional-features.md for what else you might add (messaging, once
 * you hold an API key).
 */
export function TaskList({ client }: { client: AdminApiClient | null }) {
  const { tasks, loading, error, errorStatus, performAction } = useTasks(client);

  if (error && errorStatus === 403) {
    return <PermissionDenied message={error} />;
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4 p-4">
      <h2 className="text-lg font-semibold">Tasks</h2>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {loading && <Spinner />}

      {!loading && tasks.length === 0 && !error && (
        <p className="text-sm text-slate-500">No tasks for this tenant right now.</p>
      )}

      {tasks.map((task) => (
        <div key={task.workflowId} className="rounded-md border border-slate-200 p-4">
          <div className="mb-1 flex items-center gap-2">
            <span className="font-medium">{task.title}</span>
            <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-700">{task.status}</span>
          </div>
          <p className="text-sm text-slate-500">{task.description}</p>
          {task.availableActions && task.availableActions.length > 0 && (
            <div className="mt-3 flex gap-2">
              {task.availableActions.map((action) => (
                <button
                  key={action}
                  type="button"
                  onClick={() => void performAction(task.workflowId, action)}
                  className="rounded-md border border-slate-300 px-3 py-1 text-sm hover:bg-slate-50"
                >
                  {action}
                </button>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
