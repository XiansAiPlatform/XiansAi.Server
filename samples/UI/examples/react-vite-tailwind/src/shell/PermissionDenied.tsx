/**
 * Shown in a tab's content area on a 403, since tabs are never proactively
 * disabled. See shell/Sidebar.tsx for why.
 */
export function PermissionDenied({ message }: { message?: string }) {
  return (
    <div className="rounded-md border border-amber-300 bg-amber-50 p-4 text-amber-900">
      <p className="font-medium">You don't have access to this</p>
      <p className="mt-1 text-sm">
        {message ?? 'The signed-in user does not have permission to view this.'}
      </p>
    </div>
  );
}
