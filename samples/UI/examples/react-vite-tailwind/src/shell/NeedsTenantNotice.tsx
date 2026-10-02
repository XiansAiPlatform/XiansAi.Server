/**
 * Shown on a tab that needs a tenant (Agents & Activations, Tasks) before one
 * has been selected via the Tenant tab. Distinct from PermissionDenied: this
 * means "pick a tenant first", not "you picked one but aren't allowed in".
 */
export function NeedsTenantNotice() {
  return (
    <div className="rounded-md border border-sky-300 bg-sky-50 p-4 text-sky-900">
      <p className="font-medium">Select a tenant first</p>
      <p className="mt-1 text-sm">Go to the Tenant tab and enter a tenant id to see this.</p>
    </div>
  );
}
