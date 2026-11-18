namespace BEKP.Shared.Security;

/// <summary>Role names issued by the hospital identity provider (prepared for RBAC).</summary>
public static class BekpRoles
{
    /// <summary>Read-only access to realtime dashboards (managers, head nurses, operations room).</summary>
    public const string DashboardViewer = "bekp.dashboard.viewer";

    /// <summary>System-to-system integration (HIS / LIS / blood bank) that pushes inpatient events.</summary>
    public const string Integration = "bekp.integration";

    /// <summary>Platform administrator.</summary>
    public const string Administrator = "bekp.admin";
}

/// <summary>Authorization policy names shared by API, realtime hub and tests.</summary>
public static class BekpPolicies
{
    public const string DashboardRead = "Dashboard.Read";
    public const string InpatientWrite = "Inpatient.Write";
}
