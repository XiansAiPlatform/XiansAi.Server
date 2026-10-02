import { useState } from 'react';
import { useAuth } from './auth/useAuth';
import { useAdminApiClient } from './adminApi/useAdminApiClient';
import { AppShell, type TabId } from './shell/AppShell';
import { NeedsTenantNotice } from './shell/NeedsTenantNotice';
import { Spinner } from './shell/Spinner';
import { TenantSwitcher } from './tenant/TenantSwitcher';
import { AgentsAndActivations } from './agents/AgentsAndActivations';
import { TaskList } from './tasks/TaskList';
import { getServerConfig } from './config';

export function App() {
  const auth = useAuth();

  // Seeds the Tenant tab with VITE_XIANS_TENANT_ID if set; otherwise null.
  const [activeTenantId, setActiveTenantId] = useState<string | null>(
    () => getServerConfig().tenantId ?? null
  );
  const [activeTab, setActiveTab] = useState<TabId>('tenant');

  // Hooks can't be called conditionally, so this runs before the auth
  // early-returns below; the placeholder identity is unused since
  // activeTenantId is still null whenever auth isn't ready.
  const client = useAdminApiClient(
    auth.status === 'ready' ? auth.identity : { participantId: '', getIdToken: async () => '' },
    activeTenantId
  );

  if (auth.status === 'error') {
    return (
      <div className="mx-auto mt-16 max-w-xl p-4">
        <div className="rounded-md border border-red-300 bg-red-50 p-4 text-red-900">
          <p className="font-medium">Can't sign in</p>
          <p className="mt-1 text-sm">{auth.message}</p>
        </div>
      </div>
    );
  }

  if (auth.status === 'loading') {
    return (
      <div className="flex h-screen items-center justify-center">
        <Spinner />
      </div>
    );
  }

  const { identity, signOut } = auth;

  return (
    <AppShell
      name={identity.name}
      email={identity.email}
      onSignOut={signOut}
      activeTab={activeTab}
      onTabChange={setActiveTab}
      activeTenantId={activeTenantId}
    >
      {activeTab === 'tenant' && (
        <TenantSwitcher identity={identity} activeTenantId={activeTenantId} onTenantChanged={setActiveTenantId} />
      )}
      {activeTab === 'agents' && (client ? <AgentsAndActivations client={client} /> : <NeedsTenantNotice />)}
      {activeTab === 'tasks' && (client ? <TaskList client={client} /> : <NeedsTenantNotice />)}
    </AppShell>
  );
}
