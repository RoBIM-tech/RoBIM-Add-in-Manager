using System.Text.Json;
using RoBIM.Manager.Core;

internal static class ManagerUpdateTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool condition) { if (!condition) throw new Exception("Manager update policy failed"); }
        string Release(string version,bool draft=false,bool prerelease=true,string digest="") => JsonSerializer.Serialize(new {
            tag_name="v"+version,draft,prerelease,assets=new[]{new {name="RoBIM-Manager-Setup.exe",id=123,digest=digest.Length==0 ? "sha256:"+new string('a',64) : digest}} });
        ManagerUpdate? Select(string current,params string[] releases)
        { using var json=JsonDocument.Parse("["+string.Join(',',releases)+"]"); return ManagerUpdates.Select(json.RootElement,current); }
        test("ManagerUpdateSelectsNewestVerifiedSetupIndependentOfApiOrder",()=>Require(Select("1.0.0-preview.50",Release("1.0.0-preview.52"),Release("1.0.0-preview.51"))?.Version=="1.0.0-preview.52"));
        test("ManagerUpdateNeverDowngradesOrReinstallsCurrent",()=>Require(Select("1.0.0-preview.51",Release("1.0.0-preview.50"),Release("1.0.0-preview.51")) is null));
        test("ManagerStableDoesNotAutomaticallyInstallPrerelease",()=>Require(Select("1.0.0",Release("2.0.0-test.1")) is null));
        test("ManagerPreviewCanPromoteToStable",()=>Require(Select("1.0.0-preview.51",Release("1.0.0",prerelease:false))?.Version=="1.0.0"));
        test("ManagerUpdateRejectsDraftAndMissingDigest",()=>Require(Select("1.0.0-preview.50",Release("1.0.0-preview.51",draft:true),Release("1.0.0-preview.52",digest:"invalid")) is null));
        test("ManagerAutoUpdateCannotClaimUnmanagedDirectory",()=>
        {
            var directory=Path.Combine(root,"unmanaged-manager");Directory.CreateDirectory(directory);
            Require(!ManagerUpdates.NativeInstallation(directory));
            File.WriteAllText(Path.Combine(directory,".robim-manager-native.ini"),"Product=Other\nPath="+directory);
            Require(!ManagerUpdates.NativeInstallation(directory));
        });
    }
}
