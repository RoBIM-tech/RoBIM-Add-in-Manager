using RoBIM.Cad.Configuration;

namespace RoBIM.Manager.Core;

// Prepared feature snapshots intentionally exclude the stable loader. Its identity is
// recorded in the request and must be read there, rather than as a snapshot file.
public sealed record CadButtonSource(string Root, string Bundle, CadCatalog Catalog, CadRequest? Previous)
{
    public string LoaderHash => Previous?.LoaderHash ?? CadFiles.Hash(Path.Combine(Root, "RoBIM.Cad.Loader.dll"));
    public bool NeedsConnection(ToolStatus installed) => !installed.Managed || !CadReload.Compatible(installed.Path) ||
        !CadFiles.Hash(Path.Combine(CadReload.WindowsDirectory(installed.Path), "RoBIM.Cad.Loader.dll")).Equals(LoaderHash, StringComparison.OrdinalIgnoreCase);
}
