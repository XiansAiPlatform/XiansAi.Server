import { useCallback, useEffect, useRef, useState } from 'react';
import { AdminApiError, type AdminApiClient, type AdminTask } from '../adminApi/client';

export interface TasksState {
  tasks: AdminTask[];
  loading: boolean;
  error: string | null;
  errorStatus?: number;
  refresh: () => void;
  performAction: (taskId: string, action: string, comment?: string) => Promise<void>;
}

/**
 * Tasks are reachable under keyless OIDC auth, unlike messaging
 * (docs/authentication.md). Takes the shared AdminApi client so switching
 * tenants naturally triggers a refetch; null until a tenant is selected.
 */
export function useTasks(client: AdminApiClient | null): TasksState {
  const [tasks, setTasks] = useState<AdminTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [errorStatus, setErrorStatus] = useState<number | undefined>(undefined);

  const requestId = useRef(0);

  const refresh = useCallback(() => {
    if (!client) return;
    const id = ++requestId.current;
    const isCurrent = () => id === requestId.current;
    setLoading(true);
    setError(null);
    setErrorStatus(undefined);
    client
      .listTasks({ pageSize: 20 })
      .then((result) => {
        if (isCurrent()) setTasks(result.tasks);
      })
      .catch((err) => {
        if (!isCurrent()) return;
        setError(err instanceof Error ? err.message : String(err));
        setErrorStatus(err instanceof AdminApiError ? err.status : undefined);
      })
      .finally(() => {
        if (isCurrent()) setLoading(false);
      });
  }, [client]);

  useEffect(refresh, [refresh]);

  useEffect(
    () => () => {
      requestId.current++;
    },
    []
  );

  const performAction = async (taskId: string, action: string, comment?: string) => {
    if (!client) return;
    await client.performAction(taskId, action, comment);
    refresh();
  };

  return { tasks, loading, error, errorStatus, refresh, performAction };
}
