using System.IO;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private static bool CadConfigurable(ProductRow row) => row.Status.Tool.Kind == ToolKind.AutoCAD && row.Status.Managed &&
        !row.Status.Modified && CadReload.Compatible(row.Status.Path);
    private static bool CadNeedsConnection(ProductRow row) => row.Status.Tool.Id == Catalog.Cad.Id && !row.Status.Modified &&
        !row.Status.External && !CadReload.Compatible(row.Status.Path);

    private async Task ConfigureCadButtons(bool latestPackage, string? archivePath = null)
    {
        if (!Allowed) { SwitchPage("Account"); return; }
        if (SelectedProduct() is not { } row || !CadConfigurable(row)) return;
        SourcePackage? downloaded = null;
        if (latestPackage || archivePath is not null)
        {
            await Run("CAD 기능 패키지 준비", async () => { downloaded = archivePath is not null ? await sources.OfflineCad(archivePath) : await sources.DownloadCad(CanUseTest ? null : "stable"); });
            if (downloaded is null) return;
        }
        try
        {
            var bridge = new CadReload(paths);
            var previous = bridge.Desired(row.Status.Path);
            var sourceBundle = downloaded?.Path ?? row.Status.Path;
            var sourceRequest = downloaded is null ? previous : null;
            string root;
            try
            {
                if (sourceRequest is not null && sourceRequest.LoaderHash != CadFiles.Hash(Path.Combine(CadReload.WindowsDirectory(row.Status.Path), "RoBIM.Cad.Loader.dll")))
                    sourceRequest = null;
                root = sourceRequest is null ? CadReload.WindowsDirectory(sourceBundle) : CadFiles.Snapshot(bridge.Data, sourceRequest);
            }
            catch (Exception) when (sourceRequest is not null)
            {
                sourceRequest = null; root = CadReload.WindowsDirectory(sourceBundle);
                WriteLog("이전 준비 파일을 사용할 수 없어 설치된 CAD 패키지에서 버튼 목록을 읽었습니다.");
            }
            var catalog = CadFiles.Catalog(root);
            using var dialog = new CadButtonsDialog(catalog, previous?.SelectedActions ?? [], CadReload.Sessions());
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var configured = dialog.Configuration; var selected = dialog.Selection; var session = dialog.Session;
            await Run("CAD 선택 적용 준비", async () =>
            {
                foreach (var module in configured.Modules.Where(m => m.Actions.Any(a => selected.Contains(CadFiles.Key(m, a)) &&
                    (downloaded is not null || previous?.SelectedActions.Contains(CadFiles.Key(m, a)) != true))))
                    RequireChannelAccess(ModuleReleases.Cad(root, module.Id, module.Version).Channel);
                await Task.Run(() => bridge.Prepare(row.Status.Tool, sourceBundle, configured, selected, session, sourceRequest));
                WriteLog("CAD 준비 완료 — AutoCAD의 RoBIM → 관리 → Reload에서 적용하십시오.");
                MessageBox.Show(this, "선택한 버튼과 기능을 준비했습니다.\n\nAutoCAD → RoBIM → 관리 → Reload\n\nCAD 적용 결과는 이 화면에서 확인할 수 있습니다.", Text);
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private async Task PrepareCadArchive()
    {
        using var picker = new OpenFileDialog { Title = "CAD 기능 패키지 ZIP 선택 (.sha256 파일 필요)", Filter = "CAD 패키지 ZIP|*.zip", CheckFileExists = true };
        if (picker.ShowDialog(this) == DialogResult.OK) await ConfigureCadButtons(false, picker.FileName);
    }
}
