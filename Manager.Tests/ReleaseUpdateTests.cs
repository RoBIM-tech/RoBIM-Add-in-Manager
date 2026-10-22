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
            var preview = ReleaseUpdates.Select(document.RootElement, package with { Channel = "test" });
            require(stable.AssetId == 1 && stable.Version == "v1.0.0" && preview.AssetId == 2, "Incorrect release chosen");
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
