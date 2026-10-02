import { type ReactNode } from 'react';
import { NavBar, NAVBAR_HEIGHT } from './NavBar';
import { Sidebar, DRAWER_WIDTH } from './Sidebar';
import { type TabId } from './types';

export type { TabId };

/**
 * Pure layout: nav bar + sidebar + content area. No data fetching happens
 * here; App.tsx owns the active tab/tenant state and decides what to render
 * as children.
 */
export function AppShell({
  name,
  email,
  onSignOut,
  activeTab,
  onTabChange,
  activeTenantId,
  children,
}: {
  name?: string;
  email?: string;
  onSignOut: () => void;
  activeTab: TabId;
  onTabChange: (tab: TabId) => void;
  activeTenantId: string | null;
  children: ReactNode;
}) {
  return (
    <div className="min-h-screen bg-slate-50">
      <NavBar name={name} email={email} onSignOut={onSignOut} />
      <Sidebar activeTab={activeTab} onTabChange={onTabChange} activeTenantId={activeTenantId} />
      <main style={{ marginLeft: DRAWER_WIDTH, paddingTop: NAVBAR_HEIGHT }}>{children}</main>
    </div>
  );
}
