using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

/// <summary>Only repositories transferred on 2026-10-27; unrelated publishers stay untouched.</summary>
public static class RepositoryOwnership
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "RoBIM-Workspace", "RoBIM-Libraries", "RoBIM-Add-in-Manager", "RoBIM-CAD",
        "RoBIM-Revit-Addin", "RoBIM-pyrevit", "RoBIM-pyrevit-extension", "RoBIM-Revit-Sheets",
        "RoBIM-Revit-ParameterCopy", "RoBIM-Revit-ParameterBulkEdit", "RoBIM-Revit-CentralClone",
        "RoBIM-Revit-RelinkPaths", "RoBIM-Revit-AutoJoin", "RoBIM-Revit-ViewDuplicate", "RoBIM-Revit-ViewRotation"
    };
    public static string Canonical(string repository) => ChangeOwner(repository, "gagaggan", "RoBIM-tech");
    public static string LegacyIdentity(string repository) => ChangeOwner(repository, "RoBIM-tech", "gagaggan");
    private static string ChangeOwner(string repository, string from, string to)
    {
        var parts = repository.Split('/');
        return parts.Length == 2 && parts[0].Equals(from, StringComparison.OrdinalIgnoreCase) && Names.Contains(parts[1])
            ? to + "/" + parts[1] : repository;
    }
    public static string ApiRoute(string route) => Regex.Replace(route,
        @"^/repos/(?<owner>[^/?]+)/(?<name>[^/?]+)", match =>
            "/repos/" + Canonical(match.Groups["owner"].Value + "/" + match.Groups["name"].Value), RegexOptions.IgnoreCase);
}
