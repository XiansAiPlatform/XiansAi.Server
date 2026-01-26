public static class SystemRoles
{
    public const string SysAdmin = "SysAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string TenantParticipant = "TenantParticipant";
    public const string TenantUser = "TenantUser";
}

public static class Policies
{
    public const string RequireSysAdmin = "RequireSysAdmin";
    public const string RequireTenantAdmin = "RequireTenantAdmin";
    public const string RequireTenantUser = "RequireTenantUser";
}