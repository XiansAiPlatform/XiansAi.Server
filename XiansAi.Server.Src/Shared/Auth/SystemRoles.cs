public static class SystemRoles
{
    public const string SysAdmin = "SysAdmin"; // system admin
    public const string TenantAdmin = "TenantAdmin"; // tenant admin
    public const string TenantUser = "TenantUser"; // developer
    public const string TenantParticipantAdmin = "TenantParticipantAdmin"; // agent participant admin
    public const string TenantParticipant = "TenantParticipant"; // agent participant


}

public static class Policies
{
    public const string RequireSysAdmin = "RequireSysAdmin";
    public const string RequireTenantAdmin = "RequireTenantAdmin";
    public const string RequireTenantUser = "RequireTenantUser";
}