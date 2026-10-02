import { type ServerConfig } from '../config';
import { type Identity } from '../auth/useAuth';

/**
 * The shape AdminApi returns for a task, exactly as
 * Features/AdminApi/Endpoints/AdminTaskEndpoints.cs sends it (camelCase over
 * the wire). See docs/optional-features.md for what a task is.
 */
export interface AdminTask {
  workflowId: string;
  runId: string;
  title: string;
  description: string;
  workflowStatus: string;
  timedOut: boolean;
  initialWork?: string;
  finalWork?: string;
  participantId?: string;
  status: string;
  isCompleted: boolean;
  availableActions?: string[];
  performedAction?: string;
  comment?: string;
  startTime?: string;
  closeTime?: string;
  metadata?: Record<string, unknown>;
  agentName?: string;
  activationName?: string;
  tenantId?: string;
}

export interface PaginatedTasks {
  tasks: AdminTask[];
  nextPageToken?: string;
  pageSize: number;
  hasNextPage: boolean;
  totalCount?: number;
}

export interface ListTasksParams {
  pageSize?: number;
  pageToken?: string;
  agentName?: string;
  activationName?: string;
  participantId?: string;
  status?: string;
}

/**
 * The shape AdminApi returns for an agent activation, exactly as
 * Shared/Data/Models/AgentActivation.cs sends it (camelCase over the wire).
 * One running, named instance of an agent; see docs/overview.md.
 */
export interface AgentActivation {
  id: string;
  name: string;
  agentName: string;
  description?: string;
  participantId?: string;
  createdBy: string;
  createdAt: string;
  tenantId: string;
  workflowConfiguration?: Record<string, unknown>;
  workflowIds: string[];
  active?: boolean;
  activatedAt?: string;
  deactivatedAt?: string;
}

/**
 * The shape AdminApi returns for an agent (deployment), as
 * Shared/Data/Models/Agent.cs sends it. Deliberately not exhaustive: only
 * the fields this example actually renders are listed.
 */
export interface Agent {
  id: string;
  name: string;
  tenant: string;
  originTenant?: string;
  createdBy: string;
  createdAt: string;
}

export interface Pagination {
  page: number;
  pageSize: number;
  totalPages: number;
  totalItems: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

export interface PaginatedAgentDeployments {
  agents: Agent[];
  pagination: Pagination;
}

export interface ListAgentDeploymentsParams {
  page?: number;
  pageSize?: number;
}

/**
 * An AdminApi error, carrying the HTTP status so callers can distinguish
 * "you're not allowed" (403) from any other failure without re-parsing the
 * message string. See src/shell/PermissionDenied.tsx.
 */
export class AdminApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = 'AdminApiError';
  }
}

/**
 * No SDK wraps AdminApi today (see docs/integration-options.md), so this is
 * a thin `fetch` wrapper: every call sends the signed-in user's ID token as
 * X-User-Token, no API key involved. Swap this file's internals for your
 * own HTTP client, or port the same idea to another language, freely.
 */
export function createAdminApiClient(server: ServerConfig, identity: Identity) {
  if (!server.tenantId) {
    throw new Error(
      'This example needs VITE_XIANS_TENANT_ID set. A SysAdmin caller could supply a ' +
        'tenantId per request instead; see docs/authentication.md.'
    );
  }
  const tenantId = server.tenantId;
  const base = `${server.serverUrl}/api/v1/admin/tenants/${encodeURIComponent(tenantId)}`;

  async function call<T>(path: string, init?: RequestInit): Promise<T> {
    const idToken = await identity.getIdToken();
    const response = await fetch(`${base}${path}`, {
      ...init,
      headers: {
        'X-User-Token': idToken,
        'Content-Type': 'application/json',
        ...init?.headers,
      },
    });

    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new AdminApiError(
        body.error ?? `AdminApi request failed with ${response.status}`,
        response.status
      );
    }

    return response.status === 204 ? (undefined as T) : response.json();
  }

  return {
    listTasks(params: ListTasksParams = {}): Promise<PaginatedTasks> {
      const query = new URLSearchParams();
      for (const [key, value] of Object.entries(params)) {
        if (value !== undefined) query.set(key, String(value));
      }
      const suffix = query.toString() ? `?${query}` : '';
      return call<PaginatedTasks>(`/tasks${suffix}`);
    },

    getTask(taskId: string): Promise<AdminTask> {
      return call<AdminTask>(`/tasks/by-id?taskId=${encodeURIComponent(taskId)}`);
    },

    performAction(taskId: string, action: string, comment?: string): Promise<void> {
      return call<void>(`/tasks/actions?taskId=${encodeURIComponent(taskId)}`, {
        method: 'POST',
        body: JSON.stringify({ action, comment }),
      });
    },

    // tenant.agentActivations.list: open to every tenant role (TenantParticipant
    // and up), so this is also what a tenant switcher should call to validate
    // membership without depending on a narrower role. See docs/optional-features.md.
    listAgentActivations(agentName?: string): Promise<AgentActivation[]> {
      const suffix = agentName ? `?agentName=${encodeURIComponent(agentName)}` : '';
      return call<AgentActivation[]>(`/agentActivations${suffix}`);
    },

    // tenant.agentDeployments.list: requires TenantParticipantAdmin and up;
    // excludes plain TenantParticipant. Expect this to 403 for some callers.
    listAgentDeployments(params: ListAgentDeploymentsParams = {}): Promise<PaginatedAgentDeployments> {
      const query = new URLSearchParams();
      if (params.page !== undefined) query.set('page', String(params.page));
      if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));
      const suffix = query.toString() ? `?${query}` : '';
      return call<PaginatedAgentDeployments>(`/agentDeployments${suffix}`);
    },
  };
}

export type AdminApiClient = ReturnType<typeof createAdminApiClient>;
