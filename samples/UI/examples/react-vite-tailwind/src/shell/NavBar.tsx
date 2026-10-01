export const NAVBAR_HEIGHT = 56;

/**
 * Name/email come from the ID token's own claims, no extra API call. No
 * role is shown; see src/auth/useAuth.ts for why.
 */
export function NavBar({
  name,
  email,
  onSignOut,
}: {
  name?: string;
  email?: string;
  onSignOut: () => void;
}) {
  return (
    <header
      className="fixed inset-x-0 top-0 z-20 flex items-center justify-between border-b border-slate-200 bg-white px-4"
      style={{ height: NAVBAR_HEIGHT }}
    >
      <span className="font-semibold">Xians Custom UI Example</span>
      <div className="flex items-center gap-3">
        <div className="text-right">
          <p className="text-sm">{name ?? email ?? 'Signed in'}</p>
          {name && email && <p className="text-xs text-slate-500">{email}</p>}
        </div>
        <button
          type="button"
          onClick={onSignOut}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-50"
        >
          Sign out
        </button>
      </div>
    </header>
  );
}
