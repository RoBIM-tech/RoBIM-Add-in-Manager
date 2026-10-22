using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel locationsPage = new() { Dock = DockStyle.Fill };
    private readonly TextBox pyRevitRootInput = new() { Width = 640 };
    private readonly Button applyLocation = MakeButton("이 위치로 이전 / 적용", true);
    private readonly Label locationState = new() { AutoSize = true, MaximumSize = new(870, 0), ForeColor = Color.FromArgb(75, 95, 125) };
    private readonly Label selectedExtensionPath = new() { AutoSize = true, MaximumSize = new(870, 0), Padding = new(0, 4, 0, 14) };
    private void BuildLocationsPage()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "RoBIM pyRevit 설치 폴더", AutoSize = true, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Margin = new(0, 12, 0, 12) });
        panel.Controls.Add(Note("선택한 폴더 아래에 RoBIM-Selected.extension 등 관리 확장이 만들어집니다. 다른 드라이브나 폴더를 선택할 수 있습니다."));
        var row = Row(); var browse = MakeButton("폴더 선택"); row.Controls.AddRange([pyRevitRootInput, browse]); panel.Controls.Add(row);
        panel.Controls.Add(selectedExtensionPath);
        var buttons = Row(); var defaults = MakeButton("기본 위치"); var open = MakeButton("폴더 열기"); buttons.Controls.AddRange([applyLocation, defaults, open]); panel.Controls.Add(buttons);
        panel.Controls.Add(Note("적용 시 pyRevit 확장 검색 경로에 등록합니다. 기존 Manager 관리 설치는 백업·파일 검증 후 이전하며, 다른 확장과 기존 검색 경로는 보존합니다. Revit을 종료한 상태에서 적용하세요."));
        panel.Controls.Add(locationState); locationsPage.Controls.Add(panel);
        pyRevitRootInput.TextChanged += (_, _) => selectedExtensionPath.Text = "버튼 선택 설치: " + (string.IsNullOrWhiteSpace(pyRevitRootInput.Text) ? "폴더를 선택하세요." : Path.Combine(pyRevitRootInput.Text.Trim(), Catalog.RevitSelected.FolderName));
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = "pyRevit 확장을 설치할 상위 폴더를 선택하세요.", UseDescriptionForTitle = true, SelectedPath = pyRevitRootInput.Text, ShowNewFolderButton = true }; if (dialog.ShowDialog(this) == DialogResult.OK) pyRevitRootInput.Text = dialog.SelectedPath; };
        defaults.Click += (_, _) => pyRevitRootInput.Text = paths.RevitExtensions;
        open.Click += (_, _) => OpenFolder(pyRevitRootInput.Text.Trim());
        applyLocation.Click += async (_, _) =>
        {
            var requested = pyRevitRootInput.Text.Trim();
            await Run("pyRevit 설치 위치 변경", async () =>
            {
                var result = await Task.Run(() => new PyRevitLocation(paths).Change(requested));
                developerSource = null; loadedBranch = null; panelList.Items.Clear();
                await RefreshStatuses(); ShowLocation();
                locationState.Text = $"적용 완료 · 관리 확장 {result.MovedExtensions}개 이전 · pyRevit 검색 경로 등록\n백업: {result.Backup}";
                WriteLog($"pyRevit 설치 위치 변경: {result.Root} · {result.MovedExtensions}개 이전");
            });
        };
    }
    private void ShowLocation()
    {
        pyRevitRootInput.Text = exampleLocation();
        UpdateLocationActions();
        locationState.Text = preview ? "화면 확인용 예시 · 파일을 변경하지 않습니다." : HostRunning(ToolKind.PyRevit) ? "Revit을 종료하면 설치 위치를 변경할 수 있습니다." : "현재 설치 폴더: " + PyRevitLocation.Root(paths);
        string exampleLocation() => preview ? @"D:\RoBIM\pyRevit Extensions" : PyRevitLocation.Root(paths);
    }
    private void UpdateLocationActions() => applyLocation.Enabled = !preview && !busy && !refreshingSnapshot && !HostRunning(ToolKind.PyRevit);
}
