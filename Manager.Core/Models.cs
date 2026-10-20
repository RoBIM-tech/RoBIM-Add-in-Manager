namespace RoBIM.Manager.Core;

public enum ToolKind { AutoCAD, PyRevit, NativeRevit, NativeRevitHost }

public sealed record ToolSpec(string Id, string Name, ToolKind Kind, string Repository,
    string Branch, string FolderName, string Description, bool CustomPackage = false);

public static class Catalog
{
    public static readonly ToolSpec RevitHost = new("robim-revit-host", "RoBIM 독립 Add-in 로더", ToolKind.NativeRevitHost,
        "gagaggan/RoBIM-Revit-Addin", "main", "RoBIM.Revit.Host.bundle", "실행 중 Revit의 독립 모듈 Reload 연결");
    public static readonly ToolSpec RevitSelected = new("robim-revit-selected", "RoBIM 버튼 선택 설치", ToolKind.PyRevit,
        "gagaggan/RoBIM-pyrevit", "main", "RoBIM-Selected.extension", "기존 원본과 공용 의존성을 보존하는 버튼별 설치 프로필");
    public static readonly ToolSpec Cad = new("delhidden", "RoBIM CAD 연결 모듈", ToolKind.AutoCAD,
        "gagaggan/RoBIM-CAD", "main", "DelHiddenNet.bundle",
        "AutoCAD 2026.1.2+ / .NET 10 · 최초 설치 후 필요한 버튼 선택 → CAD Reload");
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
    public string Target(ToolSpec tool) => tool.Kind == ToolKind.NativeRevitHost ? Path.Combine(Data, "NativeRevit", "Host", tool.FolderName) : Path.Combine(tool.Kind is ToolKind.AutoCAD or ToolKind.NativeRevit ? CadPlugins : tool.Id == Catalog.ReloadBridge.Id ? RevitExtensions : PyRevitLocation.Root(this), tool.FolderName);
}

public sealed record InstallRecord(string Id, string Version, string Path, DateTimeOffset InstalledAt,
    Dictionary<string, string> Hashes);
public sealed record ToolStatus(ToolSpec Tool, string Path, string Version, bool Managed, bool External,
    bool Modified, string Detail);
public sealed record BackupInfo(string Directory, InstallRecord Record);
public sealed record SourcePackage(string Path, string Version);
