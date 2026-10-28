using RoBIM.Manager.Core;

internal static class HostRequirementTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool value) { if (!value) throw new Exception("Runtime requirement check failed"); }
        var folder = Path.Combine(root, "requirements"); var host = Path.Combine(folder, "host"); var dotnet = Path.Combine(folder, "x64");
        Directory.CreateDirectory(host);
        var config = "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"frameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"},{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"10.0.0\"}]}}";
        var path = Path.Combine(host, "RevitNET.runtimeconfig.json"); File.WriteAllText(path, config);
        test("RuntimeCheckRejectsMissingHostWithoutAssumingInstalledDotNet", () => Require(!HostRequirements.Check(false, Path.Combine(folder,"missing"),dotnet).Ready));
        test("RuntimeCheckRejectsMissingDesktopDespiteCoreAndOtherMajor", () =>
        {
            Directory.CreateDirectory(Path.Combine(dotnet,"shared","Microsoft.NETCore.App","10.0.10"));
            Directory.CreateDirectory(Path.Combine(dotnet,"shared","Microsoft.WindowsDesktop.App","8.0.28"));
            Require(!HostRequirements.Check(false,host,dotnet).Ready);
        });
        test("RuntimeCheckAcceptsCompatibleX64PatchAndReportsVersion", () =>
        {
            Directory.CreateDirectory(Path.Combine(dotnet,"shared","Microsoft.WindowsDesktop.App","10.0.10"));
            var report=HostRequirements.Check(false,host,dotnet); Require(report.Ready && report.Text.Contains("10.0.10 x64"));
        });
        test("RuntimeCheckRejectsHostBeforeNet10Update", () =>
        {
            File.WriteAllText(path,config.Replace("net10.0","net8.0")); Require(!HostRequirements.Check(false,host,dotnet).Ready); File.WriteAllText(path,config);
        });
        test("RuntimeCheckRejectsMalformedConfig", () =>
        {
            File.WriteAllText(path,"{}"); Require(!HostRequirements.Check(false,host,dotnet).Ready); File.WriteAllText(path,config);
        });
        test("CadRuntimeCheckIncludesHostRequiredAspNetCore", () =>
        {
            File.WriteAllText(Path.Combine(host,"acdbmgd.runtimeconfig.json"),config.Replace("Microsoft.WindowsDesktop.App","Microsoft.AspNetCore.App"));
            Require(!HostRequirements.Check(true,host,dotnet).Ready);
            Directory.CreateDirectory(Path.Combine(dotnet,"shared","Microsoft.AspNetCore.App","10.0.10"));
            Require(HostRequirements.Check(true,host,dotnet).Ready);
        });
    }
}
