namespace RoBIM.Manager.Core;

public sealed record RevitButtonInventory(string? Root, RevitButton[] Buttons, string[] Selected, bool Pending)
{
    public static RevitButtonInventory Read(ManagerPaths paths, ToolStatus owner, SourcePackage? available = null, Func<string, bool>? isEnabled = null)
    {
        var enabled = isEnabled ?? RevitProfiles.EnabledFromUserIni;
        var pending = new RevitReload(paths).PendingSelection();
        var selected = new InstallService(paths).Status(Catalog.RevitSelected);
        var selectedActive = selected.Managed && enabled("RoBIM-Selected");
        var existing = Directory.Exists(owner.Path) ? owner.Path : null;
        var root = pending?.Path ?? (selectedActive ? selected.Path : existing ?? available?.Path);
        if (root is null) return new(null, [], [], false);
        var buttons = RevitButtons.Read(root);
        // The folder's presence establishes installation. Git/version lookup failures must not
        // turn an existing external installation into an uninstalled downloaded catalog.
        var ids = pending is not null || selectedActive ? RevitButtons.ReadSelection(root)?.Selected ?? [] :
            existing is not null && enabled("RoBIM") ? buttons.Select(x => x.Id).ToArray() : [];
        return new(root, buttons, ids, pending is not null);
    }
}
