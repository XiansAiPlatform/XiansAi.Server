import { type TabId } from './types';
import { NAVBAR_HEIGHT } from './NavBar';

export const DRAWER_WIDTH = 220;

const TABS: { id: TabId; label: string }[] = [
  { id: 'tenant', label: 'Tenant' },
  { id: 'agents', label: 'Agents & Activations' },
  { id: 'tasks', label: 'Tasks' },
];

/**
 * Tabs are always enabled: there's no capability-introspection endpoint this
 * app can call in advance (docs/authentication.md), so a lacking permission
 * shows up as PermissionDenied inside that tab instead. The active tenant is
 * pinned at the bottom since every tab depends on it.
 */
export function Sidebar({
  activeTab,
  onTabChange,
  activeTenantId,
}: {
  activeTab: TabId;
  onTabChange: (tab: TabId) => void;
  activeTenantId: string | null;
}) {
  return (
    <nav
      className="fixed inset-y-0 left-0 z-10 flex flex-col border-r border-slate-200 bg-white"
      style={{ width: DRAWER_WIDTH, paddingTop: NAVBAR_HEIGHT }}
    >
      <ul className="p-2">
        {TABS.map((tab) => (
          <li key={tab.id}>
            <button
              type="button"
              onClick={() => onTabChange(tab.id)}
              className={`w-full rounded-md px-3 py-2 text-left text-sm ${
                activeTab === tab.id
                  ? 'bg-slate-900 text-white'
                  : 'text-slate-700 hover:bg-slate-100'
              }`}
            >
              {tab.label}
            </button>
          </li>
        ))}
      </ul>

      <div className="mt-auto border-t border-slate-200 p-3">
        <p className="text-xs uppercase tracking-wide text-slate-400">Active tenant</p>
        <p className="truncate text-sm font-medium text-slate-700">{activeTenantId ?? 'None selected'}</p>
      </div>
    </nav>
  );
}
