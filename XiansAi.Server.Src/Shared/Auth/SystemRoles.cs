public static class SystemRoles
{
    public const string SysAdmin = "SysAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string TenantParticipant = "TenantParticipant";
    public const string TenantParticipantAdmin = "TenantParticipantAdmin";
    public const string TenantUser = "TenantUser";

    /// <summary>
    /// Drops the participant roles from the set an authenticated caller acts with. A participant is
    /// someone an agent can hold a conversation with rather than someone who signs in, so the role
    /// exists to be queried through the Admin API, not to carry access on a request.
    /// </summary>
    public static List<string> ExcludingParticipantRoles(IEnumerable<string> roles) =>
        roles.Where(role => role != TenantParticipant && role != TenantParticipantAdmin).ToList();
}

public static class Policies
{
    public const string RequireSysAdmin = "RequireSysAdmin";
    public const string RequireTenantAdmin = "RequireTenantAdmin";
    public const string RequireTenantUser = "RequireTenantUser";
}