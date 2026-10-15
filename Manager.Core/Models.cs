namespace RoBIM.Manager.Core;

public enum ToolKind { AutoCAD, PyRevit }

public sealed record ToolSpec(string Id, string Name, ToolKind Kind, string Repository,
    string Branch, string FolderName, string Description, bool CustomPackage = false);

public static class Catalog
{
    public static readonly ToolSpec Cad = new("delhidden", "DELHIDDEN .NET", ToolKind.AutoCAD,
        "gagaggan/RoBIM-CAD", "main", "DelHiddenNet.bundle",
        "AutoCAD 2026.1.2+ / .NET 10 · 꺼짐·동결 레이어 정리 · DHNET");
    public static readonly ToolSpec Revit = new("robim-revit", "RoBIM for Revit", ToolKind.PyRevit,
        "gagaggan/RoBIM-pyrevit", "main", "RoBIM.extension",
        "pyRevit 확장 · 기본·도면·관리·TEST 탭 · pyRevit 사전 설치 필요");
    public static readonly ToolSpec RevitDeveloper = new("robim-revit-developer", "RoBIM TEST", ToolKind.PyRevit,
        "gagaggan/RoBIM-pyrevit", "main", "RoBIM-Developer.extension",
        "선택한 개발 브랜치와 개인 TEST 패널 · 운영본과 별도 설치");
    public static readonly IReadOnlyList<ToolSpec> Tools = [Cad, Revit];
    public static readonly ToolSpec ReloadBridge = new("robim-reload-bridge", "RoBIM Reload 연결", ToolKind.PyRevit,
        "gagaggan/RoBIM-CAD", "main", "RoBIM Manager.extension", "Revit 안에서 준비된 업데이트 적용 및 pyRevit Reload");
}

public sealed record ManagerPaths(string Data, string CadPlugins, string RevitExtensions)
{
    public static ManagerPaths CurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoBIM", "Manager"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "Extensions"));
    public string Target(ToolSpec tool) => Path.Combine(tool.Kind == ToolKind.AutoCAD ? CadPlugins : RevitExtensions, tool.FolderName);
}

public sealed record InstallRecord(string Id, string Version, string Path, DateTimeOffset InstalledAt,
    Dictionary<string, string> Hashes);
public sealed record ToolStatus(ToolSpec Tool, string Path, string Version, bool Managed, bool External,
    bool Modified, string Detail);
public sealed record BackupInfo(string Directory, InstallRecord Record);
public sealed record SourcePackage(string Path, string Version);
