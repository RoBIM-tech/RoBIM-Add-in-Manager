using System.IO;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    internal sealed record PackageRow(ExtensionRepository Repository, ExtensionPackage Package);

    private async Task RefreshAccountAndCatalog()
    {
        await RefreshAccount();
        if (!Allowed) return;
        try { await RefreshExtensionCatalog(); }
        catch (Exception ex) { WriteLog("저장된 목록 유지 · 저장소 조회 실패: " + ex.Message); }
    }

    private async Task RefreshExtensionCatalog()
    {
        repositoryLookupCount++;
        var result = await Task.Run(() => new RepositoryDiscovery(paths).Refresh());
        availablePackages.Clear();
        foreach (var repository in result.Repositories)
            foreach (var package in repository.Packages) availablePackages.Add(new PackageRow(repository, package));
        await RefreshProductListAsync();
        foreach (var warning in result.Warnings) WriteLog(warning);
        if (teamAccess is not null) { await teamAccess.RefreshTools(); BackendChanged?.Invoke(); }
    }

    private async Task ManageExtension(bool restoreBackup, PackageRow? selected)
    {
        if (selected is not { } row) return;
        var tool = ExtensionCatalog.Tool(row.Repository, row.Package);
        if (tool.Kind == ToolKind.PyRevit) return;
        if (tool.Kind == ToolKind.NativeRevit)
        {
            var owner = new ProductRow(installer.Status(tool), row);
            var card = ReadCommandInventory([owner]).Single();
            if (restoreBackup) { ShowInfo("도구는 해당 카드에서 이전 버전 ZIP을 가져와 설치하십시오."); return; }
            await ToggleButton(card, false);
            return;
        }
        if (!tool.CustomPackage) { ShowInfo("RoBIM 기본 도구는 사용자 도구 화면에서 관리하십시오."); return; }
        if (!Confirm(row.Package.Name + (restoreBackup ? "의 이전 버전을 복원합니까?" : "을 제거합니까?") + "\n" + paths.Target(tool))) return;
        await Run(restoreBackup ? "확장 복원" : "확장 제거", async () =>
        {
            if (restoreBackup)
            {
                var backup = installer.Backups(tool).FirstOrDefault() ?? throw new IOException("복원할 백업이 없습니다.");
                RequirePackageAccess(tool, new SourcePackage(Path.Combine(backup.Directory, "content"), backup.Record.Version));
                await Task.Run(() => installer.Restore(tool, backup));
            }
            else await Task.Run(() => installer.Remove(tool));
            await RefreshExtensionCatalog();
        });
    }
}
