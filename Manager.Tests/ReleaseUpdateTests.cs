using System.Text.Json;
using RoBIM.Manager.Core;

static class ReleaseUpdateTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require, string root)
    {
        foreach (var (old, channel, next, target, expected) in new[] {
            ("0.2.0", "stable", "v0.3.0", "stable", true),
            ("0.9.0", "stable", "0.10.0", "stable", true),
            ("1.0.0-test.2", "test", "v1.0.0-test.10", "test", true),
            ("1.0.0-test.10", "test", "1.0.0-test.2", "test", false),
            ("1.0.0-test2", "test", "v1.0.0-test10", "test", true),
            ("1.0.0-test10", "test", "1.0.0-test2", "test", false),
            ("1.0.0-TEST2", "test", "v1.0.0-TEST10", "test", true),
            ("1.0.0-TEST10", "test", "1.0.0-TEST2", "test", false),
            ("1.0.0-test-2", "test", "v1.0.0-test-10", "test", true),
            ("1.0.0-test-10", "test", "1.0.0-test-2", "test", false),
            ("1.0.0-test.2", "test", "1.0.0", "stable", true),
            ("1.0.0", "stable", "2.0.0-test.1", "test", false),
            ("2.0.0", "stable", "1.0.0", "stable", false),
            ("1.0.0+old", "stable", "1.0.0+new", "stable", false),
            ("", "", "1.0.0", "stable", false),
            ("동봉 abcdef", "", "1.0.0", "stable", false),
            ("main@aaaa11112222", "", "main@bbbb11112222", "unknown", true),
            ("codex/feature@aaaa11112222", "", "main@bbbb11112222", "unknown", false) })
            test("ReleaseVersion:" + old + "→" + next, () => require(ReleaseUpdates.IsNewer(old, channel, new(next, target)) == expected, "Version/channel comparison"));
        test("RefreshMetadataChoosesMatchingChannelAndAssetWithoutPayload", () =>
        {
            object Asset(string name, long id) => new { name, id, size = 100, digest = "sha256:" + new string('a', 64) };
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new[] {
                new { draft = true, prerelease = false, tag_name = "v99.0.0", assets = new[] { Asset("tool.zip", 99) } },
                new { draft = false, prerelease = true, tag_name = "v2.0.0-test.1", assets = new[] { Asset("tool.zip", 2) } },
                new { draft = false, prerelease = false, tag_name = "v1.0.0", assets = new[] { Asset("other.zip", 3), Asset("tool.zip", 1) } } }));
            var package = new ExtensionPackage("tool", "tool", "revit", ".", "Tool.bundle", "release", "tool.zip", Channel: "stable");
            var stable = ReleaseUpdates.Select(document.RootElement, package);
            var requested = ExtensionCatalog.ReleaseChannel(package, "test");
            var preview = ReleaseUpdates.Select(document.RootElement, requested);
            require(stable.AssetId == 1 && stable.Version == "v1.0.0" && preview.AssetId == 2, "Incorrect release chosen");
            require(package.Channel == "stable" && requested with { Channel = "stable" } == package, "Explicit TEST choice changed the registered package identity/default");
            require(ReleaseUpdates.Select(document.RootElement, ExtensionCatalog.ReleaseChannel(requested, "stable")).AssetId == 1, "Return to stable did not select stable release");
            var legacy = ReleaseUpdates.Select(document.RootElement, package with { Channel = "unknown" });
            require(legacy.AssetId == 2 && legacy.Channel == "test", "Legacy prerelease ZIP was ignored or mislabeled stable");
            using var stableOnly = JsonDocument.Parse("[" + document.RootElement[2].GetRawText() + "]");
            var legacyStable = ReleaseUpdates.Select(stableOnly.RootElement, package with { Channel = "unknown" });
            require(legacyStable.AssetId == 1 && legacyStable.Channel == "stable", "Verified public release retained unknown channel");
        });
        test("StableRepositoryAlsoDiscoversNewTestWithoutChangingDefault", () =>
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new[] {
                new { draft=false, prerelease=true, tag_name="v0.3.0-test.1", assets=new[]{new{name="tool.zip",id=3,size=100,digest="sha256:"+new string('b',64)}} },
                new { draft=false, prerelease=false, tag_name="v0.2.0", assets=new[]{new{name="tool.zip",id=2,size=100,digest="sha256:"+new string('a',64)}} } }));
            var package = new ExtensionPackage("tool","tool","revit",".","Tool.bundle","release","tool.zip",Channel:"stable");
            var channels = ReleaseUpdates.SelectChannels(document.RootElement,package);
            require(channels.Stable?.AssetId == 2 && channels.Test?.AssetId == 3 && channels.HasNewTest("0.2.0"), "Both channels were not discovered");
            require(package.Channel == "stable" && !ReleaseUpdates.IsNewer("0.2.0","stable",channels.Test!), "Discovery switched stable to TEST");
            require(!channels.HasNewTest("0.4.0"), "Older TEST advertised against newer installed stable");
        });
        test("PublishedStableSupersedesOlderTestAndMissingTestIsNotFailure", () =>
        {
            require(!new ReleaseChannels(new("0.3.0","stable"),new("0.3.0-test.2","test")).HasNewTest(),"Obsolete prerelease advertised");
            using var empty = JsonDocument.Parse("[]");
            var channels=ReleaseUpdates.SelectChannels(empty.RootElement,new("tool","tool","revit",".","Tool.bundle","release","tool.zip",Channel:"stable"));
            require(channels.Stable is null && channels.Test is null && channels.TestError is null && !channels.HasNewTest(),"No TEST was treated as failed check");
        });
        test("BrokenTestAssetDoesNotHideValidStableRelease", () =>
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new[] {
                new {draft=false,prerelease=true,tag_name="v0.3.0-test.1",assets=new[]{new{name="tool.zip",id=3,size=100,digest="invalid"}}},
                new {draft=false,prerelease=false,tag_name="v0.2.0",assets=new[]{new{name="tool.zip",id=2,size=100,digest="sha256:"+new string('a',64)}}} }));
            var channels=ReleaseUpdates.SelectChannels(document.RootElement,new("tool","tool","revit",".","Tool.bundle","release","tool.zip",Channel:"stable"));
            require(channels.Stable?.AssetId==2 && channels.StableError is null && channels.Test is null && channels.TestError is not null,"Broken TEST suppressed stable check");
        });
        test("ExplicitReleaseChannelRejectsUnsupportedSourcesAndChannels", () =>
        {
            var native = new ExtensionPackage("tool", "tool", "revit", ".", "Tool.bundle", "release", "tool.zip", Channel: "stable");
            foreach (var (package, channel) in new[] { (native, "unknown"), (native with { Host = "autocad" }, "test"), (native with { Distribution = "git" }, "test") })
            {
                var rejected = false;
                try { ExtensionCatalog.ReleaseChannel(package, channel); } catch (IOException) { rejected = true; }
                require(rejected, "Unsupported channel override accepted");
            }
        });
        test("CadUpdateVersionFollowsActualSelectedDllAcrossRestart", () =>
        {
            var directory = Path.Combine(root, "cad-version-proof"); Directory.CreateDirectory(directory);
            var target = new ManagerPaths(Path.Combine(directory,"data"), Path.Combine(directory,"cad"), Path.Combine(directory,"revit"));
            var module = new RoBIM.Cad.Configuration.CadModule { Id = "feature", Assembly = "feature.dll" };
            var catalog = new RoBIM.Cad.Configuration.CadCatalog { Modules = [module] };
            File.WriteAllText(Path.Combine(directory, "feature.dll"), "verified new payload");
            var source = new CadButtonSource(directory, directory, catalog, null);
            new CadReleaseVersions(target).Record(source,"v1.2.0");
            require(new CadReleaseVersions(target).Selected(source,module,"v1.0.0") == "v1.2.0", "Restart lost selected feature version");
            File.WriteAllText(Path.Combine(directory,"feature.dll"),"older or modified payload");
            require(new CadReleaseVersions(target).Selected(source,module,"v1.0.0") == "v1.0.0", "Old DLL was mislabeled as newer release");
        });
        test("RefreshRejectsUnverifiedAsset", () =>
        {
            using var document = JsonDocument.Parse("[{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.0.0\",\"assets\":[{\"name\":\"tool.zip\",\"id\":1,\"size\":100}]}]");
            var rejected = false;
            try { ReleaseUpdates.Select(document.RootElement, new("tool", "tool", "revit", ".", "Tool.bundle", "release", "tool.zip", Channel: "stable")); }
            catch (IOException) { rejected = true; }
            require(rejected, "No digest accepted");
        });
    }
}
