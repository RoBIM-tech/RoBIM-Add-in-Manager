using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoBIM.Manager.Core;

internal static class RepositoryOwnershipTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
        test("TransferredPrivateApiRoutesUseNewOwnerBeforeAuthentication", () =>
        {
            Require(RepositoryOwnership.ApiRoute("/repos/gagaggan/RoBIM-CAD/releases/assets/123?x=1") == "/repos/RoBIM-tech/RoBIM-CAD-Loader/releases/assets/123?x=1", "Asset route not canonicalized");
            Require(RepositoryOwnership.ApiRoute("/repos/gagaggan/RoBIM-Revit-Sheets") == "/repos/RoBIM-tech/RoBIM-Revit-Sheets", "Metadata route not canonicalized");
            foreach (var route in new[] { "/repos/other/RoBIM-CAD", "/repos/gagaggan/Other", "/repos/gagaggan/RoBIM-Future", "/user/repos" })
                Require(RepositoryOwnership.ApiRoute(route) == route, "Untransferred repository was redirected");
        });
        test("OrganizationTransferKeepsExistingToolRecordKeys", () =>
        {
            foreach (var repo in BundledCatalog.Read().Repositories)
                foreach (var package in repo.Packages)
                {
                    var oldRepo = repo with { Repository = RepositoryOwnership.LegacyIdentity(repo.Repository) };
                    var current = ExtensionCatalog.Tool(repo, package); var old = ExtensionCatalog.Tool(oldRepo, package);
                    Require(old.Id == current.Id, "Owner move changed record key: " + package.Id);
                    if (package.Id == Catalog.Cad.Id) Require(current.Id == Catalog.Cad.Id, "CAD fixed identity changed");
                    else
                    {
                        var legacy = package.Id == "robim-revit-sheets" ? "gagaggan/RoBIM-Revit-Addin" : oldRepo.Repository;
                        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.ToLowerInvariant() + "/" + package.Id))).ToLowerInvariant()[..16];
                        Require(current.Id == "ext-" + digest, "Historical installation identity lost: " + package.Id);
                    }
                }
        });
        test("OldRegistryAndDiscoveryCacheMergeWithoutDuplicateCards", () =>
        {
            var directory = Path.Combine(root, "ownership-cache"); Directory.CreateDirectory(directory);
            var paths = new ManagerPaths(directory, Path.Combine(root, "cad"), Path.Combine(root, "revit"));
            var defaults = BundledCatalog.Read().Repositories;
            var old = defaults.Select(repo => repo with { Repository = RepositoryOwnership.LegacyIdentity(repo.Repository) }).ToArray();
            var registry = Path.Combine(directory, "extension-repositories.json"); File.WriteAllText(registry, JsonSerializer.Serialize(old));
            File.WriteAllText(Path.Combine(directory, "discovered-repositories.json"), JsonSerializer.Serialize(new RepositoryDiscoveryCache(1, old, [])));
            var bytes = File.ReadAllBytes(registry); var result = new ExtensionCatalog(paths).Registered();
            Require(result.Length == defaults.Length && result.All(repo => repo.Repository.StartsWith("RoBIM-tech/", StringComparison.Ordinal)), "Duplicate/old cards survived normalization");
            Require(bytes.SequenceEqual(File.ReadAllBytes(registry)), "Reading catalog rewrote user registrations");
        });
        test("PersonalPublisherScopeMigratesWithoutChangingUserFile", () =>
        {
            var directory = Path.Combine(root, "ownership-scope"); Directory.CreateDirectory(directory);
            var paths = new ManagerPaths(directory, Path.Combine(root, "cad"), Path.Combine(root, "revit"));
            var discovery = new RepositoryDiscovery(paths);
            discovery.SaveSettings(new(1, false, [new("gagaggan", "user")]));
            var file = Path.Combine(directory, "repository-publishers.json"); var before = File.ReadAllBytes(file);
            var settings = discovery.Settings();
            Require(!settings.IncludeConnectedAccount && settings.Publishers.Single().Name == "RoBIM-tech" && settings.Publishers[0].Kind == "organization", "Old publisher scope hid transferred repositories");
            Require(before.SequenceEqual(File.ReadAllBytes(file)), "Reading search scope rewrote preferences");
        });
        test("ExplicitOrganizationDisableIsPreserved", () =>
        {
            var directory = Path.Combine(root, "ownership-disabled"); Directory.CreateDirectory(directory);
            var discovery = new RepositoryDiscovery(new(directory, Path.Combine(root, "cad"), Path.Combine(root, "revit")));
            discovery.SaveSettings(new(1, false, [new("gagaggan"), new("RoBIM-tech", "organization", false)]));
            Require(!discovery.Settings().Publishers.Single(p => p.Name == "RoBIM-tech").Enabled, "Explicit organization preference was overridden");
        });
    }

    public static async Task Live(string destination)
    {
        var paths = ManagerPaths.CurrentUser(); WindowsDataPath.EnsureDirectory(paths.Data);
        var backup = destination + ".metadata-before"; Directory.CreateDirectory(backup);
        foreach (var name in new[] { "extension-repositories.json", "discovered-repositories.json", "repository-publishers.json" })
            if (File.Exists(Path.Combine(paths.Data, name))) File.Copy(Path.Combine(paths.Data, name), Path.Combine(backup, name), true);
        var user = await GitHubSession.Identify();
        var result = await new RepositoryDiscovery(paths).Refresh();
        var repositories = result.Repositories.Select(repo => new { repo.Repository, Packages = repo.Packages.Select(package => new { package.Id, package.ModuleId, package.CommandId, package.Host }).ToArray() }).ToArray();
        File.WriteAllText(destination, JsonSerializer.Serialize(new { Login = user.Login, Repositories = repositories, result.Warnings }));
        Console.WriteLine($"GitHub {user.Login}: {repositories.Length} repositories, {result.Warnings.Length} warnings. Metadata backup: {backup}");
    }
}
