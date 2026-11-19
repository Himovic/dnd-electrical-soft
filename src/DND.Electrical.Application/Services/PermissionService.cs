namespace DND.Electrical.Application.Services;

public enum UserRole { Viewer, Engineer, Administrator }

public enum AppPermission { ViewProjects, EditProjects, ManageLibraries, ManageDocumentation, ManagePlugins, ManageSettings }

/// <summary>Role-based permissions (local deployments use the role from user settings; central deployments from authentication).</summary>
public sealed class PermissionService(UserRole role)
{
    public UserRole Role { get; set; } = role;

    public bool Has(AppPermission p) => p switch
    {
        AppPermission.ViewProjects => true,
        AppPermission.EditProjects => Role >= UserRole.Engineer,
        AppPermission.ManageLibraries => Role >= UserRole.Engineer,
        _ => Role == UserRole.Administrator,
    };

    public void Demand(AppPermission p)
    {
        if (!Has(p)) throw new UnauthorizedAccessException($"The role '{Role}' does not allow '{p}'.");
    }
}
