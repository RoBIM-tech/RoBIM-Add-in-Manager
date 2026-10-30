using System.Text.Json;
using RoBIM.Manager.Core;

internal static class LoaderActionTests
{
    public static void Run(Action<string, Action> test)
    {
        void Require(bool value,string message){if(!value)throw new Exception(message);}
        ToolStatus State(string version="1.0.2-test.2",bool modified=false) => new(Catalog.Cad,"fixture",version,true,false,modified,"");
        var channels=new ReleaseChannels(new("1.0.3","stable"),new("1.1.0-test.3","test"));
        test("LoaderScreenshotVersionsEnableTestUpgrade",()=>{
            var result=LoaderActions.Evaluate(State(),channels,"test",true);
            Require(result.Enabled && result.Text=="업데이트","Screenshot version pair must enable update");
        });
        test("LoaderModifiedInstallExplainsProtectionInsteadOfUpdate",()=>{
            var result=LoaderActions.Evaluate(State(modified:true),channels,"test",true);
            Require(!result.Enabled && result.Text=="적용 차단" && result.Detail.Contains("변경"),"Modified reason hidden");
        });
        test("LoaderStableLatestExplainsOptionalTest",()=>{
            var result=LoaderActions.Evaluate(State("1.0.3"),channels,"stable",true);
            Require(!result.Enabled && result.Detail.Contains("TEST를 선택"),"Optional TEST instruction missing");
        });
        test("LoaderUnverifiedAndUnavailableVersionsHaveDifferentReasons",()=>{
            Require(LoaderActions.Evaluate(State(),null,"test",true).Text=="버전 확인 필요","Unchecked mislabeled latest");
            Require(LoaderActions.Evaluate(State(),new(null,null),"test",true).Text=="제공 버전 없음","Missing offer mislabeled latest");
            Require(LoaderActions.Evaluate(State("동봉 unknown"),channels,"test",true).Text=="버전 확인 필요","Unknown version mislabeled latest");
        });
        test("LoaderNormalRoleDoesNotSeeTestSuggestionOrInstall",()=>{
            Require(!LoaderActions.Evaluate(State(),channels,"test",false).Enabled,"TEST access permitted");
            Require(!LoaderActions.Evaluate(State("1.0.3"),channels,"stable",false).Detail.Contains("TEST"),"Normal role sees TEST suggestion");
        });
        test("LoaderNewArchiveNameRetainsOldStableAndTestLookup",()=>{
            string Asset(string name,int id)=>JsonSerializer.Serialize(new{name,id,size=10,digest="sha256:"+new string('a',64)});
            using var releases=JsonDocument.Parse("[{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.0.3\",\"assets\":["+Asset(CadLoaderAssets.LegacyName,1)+"]},{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v1.1.0-test.2\",\"assets\":["+Asset(CadLoaderAssets.LegacyName,2)+"]},{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v1.1.0-test.3\",\"assets\":["+Asset(CadLoaderAssets.Name,3)+"]}]");
            Require(Sources.LoaderOffer(releases.RootElement,CadLoaderAssets.Name,"stable")?.AssetId==1,"Legacy stable lost");
            Require(Sources.LoaderOffer(releases.RootElement,CadLoaderAssets.Name,"test")?.AssetId==3,"New TEST not chosen by version");
        });
        test("LoaderArchiveRenameKeepsRegistrationAndRejectsOtherAssetChanges",()=>{
            var old=new ExtensionPackage("delhidden","CAD","autocad",Catalog.Cad.FolderName,Catalog.Cad.FolderName,"release",CadLoaderAssets.LegacyName);
            var current=old with{Asset=CadLoaderAssets.Name};
            var repo=new ExtensionRepository(Catalog.Cad.Repository,"main",[current],"fixture");
            Require(ExtensionCatalog.CurrentPackage(old,repo)==current && ExtensionCatalog.Tool(repo,current).Id==Catalog.Cad.Id,"Rename changed installation identity");
            var rejected=false;try{ExtensionCatalog.CurrentPackage(old,repo with{Packages=[current with{Asset="other.zip"}]});}catch(IOException){rejected=true;}
            Require(rejected,"Arbitrary asset migration accepted");
        });
    }
}
