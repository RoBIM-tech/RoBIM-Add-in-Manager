using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record HostRequirementReport(string Host, string[] Details, string[] Problems)
{
    public bool Ready => Problems.Length == 0;
    public string Text => Host + " · " + (Ready ? "실행 환경 확인 완료" : "설치 확인 필요") + "\n" + string.Join("\n", Details.Concat(Problems));
}

public static class HostRequirements
{
    public static string DotNetRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
                if (key?.GetValue("InstallLocation") is string location && Path.IsPathFullyQualified(location)) return location;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }
    public static string HostRoot(bool cad)
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", cad ? "AutoCAD 2026" : "Revit 2026");
        var runtime = cad ? "acdbmgd.runtimeconfig.json" : "RevitNET.runtimeconfig.json";
        var settings=SafeFiles.Child(ManagerPaths.CurrentUser().Data,"host-locations.json");
        if (File.Exists(settings))
        {
            var saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(settings));
            if (saved?.TryGetValue(cad ? "autocad" : "revit",out var selected)==true && Path.IsPathFullyQualified(selected) && File.Exists(Path.Combine(selected,runtime))) return selected;
        }
        if (File.Exists(Path.Combine(fallback,runtime))) return fallback;
        var driveRoot=Path.GetPathRoot(fallback)!;
        var driveInstall=Path.Combine(driveRoot,cad ? "AutoCAD 2026" : "Revit 2026");
        if (File.Exists(Path.Combine(driveInstall,runtime))) return driveInstall;
        if (OperatingSystem.IsWindows())
        {
            try
            {
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,Microsoft.Win32.RegistryView.Registry64);
            using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (var name in uninstall?.GetSubKeyNames() ?? [])
            {
                try
                {
                    using var key = uninstall!.OpenSubKey(name);
                    var display = key?.GetValue("DisplayName") as string ?? "";
                    if (!(cad ? display.StartsWith("Autodesk AutoCAD 2026",StringComparison.OrdinalIgnoreCase) : display.Equals("Autodesk Revit 2026",StringComparison.OrdinalIgnoreCase))) continue;
                    var location = key?.GetValue("InstallLocation") as string;
                    if (location is not null && Path.IsPathFullyQualified(location) && File.Exists(Path.Combine(location,runtime))) return location;
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return fallback;
    }
    public static void SaveLocation(bool cad, string directory)
    {
        directory=Path.GetFullPath(directory); SafeFiles.NoLinks(directory);
        if (!File.Exists(Path.Combine(directory,cad ? "acad.exe" : "Revit.exe")) ||
            !File.Exists(Path.Combine(directory,cad ? "acdbmgd.runtimeconfig.json" : "RevitNET.runtimeconfig.json")))
            throw new IOException("프로그램 실행 파일과 실행 환경 설정이 있는 설치 폴더를 선택하세요.");
        var data=ManagerPaths.CurrentUser().Data; Directory.CreateDirectory(data);
        var file=SafeFiles.Child(data,"host-locations.json");
        var values=File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file)) ?? new() : new Dictionary<string,string>();
        values[cad ? "autocad" : "revit"]=directory;
        var temp=file+"."+Guid.NewGuid().ToString("N"); File.WriteAllText(temp,JsonSerializer.Serialize(values)); File.Move(temp,file,true);
    }
    public static HostRequirementReport Check(bool cad, string? hostRoot = null, string? dotnetRoot = null)
    {
        var host = cad ? "AutoCAD 2026.1.2+" : "Revit 2026.5+";
        var details = new List<string>(); var problems = new List<string>();
        var runtimeFile = Path.Combine(hostRoot ?? HostRoot(cad), cad ? "acdbmgd.runtimeconfig.json" : "RevitNET.runtimeconfig.json");
        if (!File.Exists(runtimeFile)) return new(host, [], [host + " 실행 설정을 찾지 못했습니다. 설치 경로를 확인하세요: " + runtimeFile]);
        details.Add("설치 경로: " + Path.GetDirectoryName(runtimeFile));
        var executable=Path.Combine(Path.GetDirectoryName(runtimeFile)!,cad ? "acad.exe" : "Revit.exe");
        if (File.Exists(executable)) details.Add("설치 버전: " + System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(runtimeFile));
            var options = json.RootElement.GetProperty("runtimeOptions");
            var target=options.GetProperty("tfm").GetString();
            if (target != "net10.0") problems.Add(host + "의 .NET 10 지원 업데이트가 필요합니다. 현재 호스트: " + target + ". .NET 10만 별도로 설치해도 호스트의 실행 환경은 변경되지 않습니다.");
            var frameworks = options.TryGetProperty("frameworks", out var list) ? list.EnumerateArray().ToArray() :
                options.TryGetProperty("framework", out var one) ? [one] : [];
            if (frameworks.Length == 0) problems.Add("호스트의 .NET 실행 환경 요구 사항을 확인할 수 없습니다.");
            foreach (var framework in frameworks)
            {
                var name = framework.GetProperty("name").GetString() ?? "";
                if (name is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App" or "Microsoft.AspNetCore.App"))
                    throw new InvalidDataException();
                var required = Version.Parse(framework.GetProperty("version").GetString()!);
                var directory = Path.Combine(dotnetRoot ?? DotNetRoot(), "shared", name);
                var installed = Directory.Exists(directory) ? Directory.GetDirectories(directory).Select(Path.GetFileName)
                    .Select(x => Version.TryParse(x, out var version) ? version : null)
                    .Where(x => x is not null && x.Major == required.Major && x.Minor == required.Minor && x >= required)
                    .OrderDescending().FirstOrDefault() : null;
                var label = name == "Microsoft.WindowsDesktop.App" ? ".NET Desktop Runtime" : name == "Microsoft.AspNetCore.App" ? "ASP.NET Core Runtime" : ".NET Runtime";
                if (installed is null) problems.Add($"{label} {required.Major}.{required.Minor} x64 설치가 필요합니다 (최소 {required}).");
                else details.Add($"{label} {installed} x64 확인");
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidDataException or InvalidOperationException)
        { problems.Add("호스트의 실행 환경 설정을 읽지 못했습니다. Autodesk 설치 복구를 확인하세요."); }
        return new(host, details.ToArray(), problems.ToArray());
    }
    public static void Ensure(bool cad)
    {
        var report = Check(cad);
        if (!report.Ready) throw new IOException(report.Text + "\nMicrosoft 공식 .NET 다운로드 또는 Autodesk 설치 복구를 이용하세요.");
    }
}
