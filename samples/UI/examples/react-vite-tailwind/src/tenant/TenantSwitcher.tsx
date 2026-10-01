import { type Identity } from '../auth/useAuth';
import { useTenantSwitcher } from './useTenantSwitcher';

/**
 * Free-text entry, see useTenantSwitcher.ts for why there's no picker. The
 * default first tab after login, since the other two depend on a tenant
 * being selected here first.
 */
export function TenantSwitcher({
  identity,
  activeTenantId,
  onTenantChanged,
}: {
  identity: Identity;
  activeTenantId: string | null;
  onTenantChanged: (tenantId: string) => void;
}) {
  const { input, setInput, status, error, submit } = useTenantSwitcher(identity, activeTenantId, onTenantChanged);

  return (
    <div className="mx-auto max-w-lg space-y-4 p-4">
      <h2 className="text-lg font-semibold">Tenant</h2>
      <p className="text-sm text-slate-500">
        Enter the id of the tenant to work in. The Agents & Activations and Tasks tabs
        are scoped to whichever tenant is active here.
      </p>

      {activeTenantId && (
        <div className="rounded-md border border-emerald-300 bg-emerald-50 p-3 text-sm text-emerald-900">
          Currently connected to tenant: {activeTenantId}
        </div>
      )}

      <div className="flex gap-2">
        <input
          type="text"
          placeholder="Tenant id"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void submit();
          }}
          className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
        />
        <button
          type="button"
          disabled={!input.trim() || status === 'validating'}
          onClick={() => void submit()}
          className="shrink-0 rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white disabled:cursor-not-allowed disabled:opacity-40"
        >
          {status === 'validating' ? 'Checking...' : 'Go to tenant'}
        </button>
      </div>

      {status === 'error' && error && (
        <div className="rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-900">{error}</div>
      )}
    </div>
  );
}
