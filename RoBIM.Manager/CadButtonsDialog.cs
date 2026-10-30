using System.Drawing;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed class CadButtonsDialog : Form
{
    public void RenderPreview(string destination)
    {
        StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
        Show(); Application.DoEvents(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png); Close();
    }
    private readonly CadCatalog original;
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
    private readonly ComboBox sessions = new() { Width = 660, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label" };
    private sealed record SessionItem(CadSession? Session, string Label);
    public CadSession? Session => (sessions.SelectedItem as SessionItem)?.Session;
    public IReadOnlyList<string> Selection => grid.Rows.Cast<DataGridViewRow>()
        .Where(r => r.Cells["Use"].Value is true).Select(r => (string)r.Tag!).ToArray();
    public CadCatalog Configuration => original with
    {
        Modules = original.Modules.Select(m => m with { Actions = m.Actions.Select(a =>
        {
            var row = grid.Rows.Cast<DataGridViewRow>().Single(r => (string)r.Tag! == CadFiles.Key(m, a));
            return a with { Title = Convert.ToString(row.Cells["Title"].Value)?.Trim() ?? "",
                Panel = Convert.ToString(row.Cells["Panel"].Value)?.Trim() ?? "", Order = Convert.ToInt32(row.Cells["Order"].Value) };
        }).ToList() }).ToList()
    };

    public CadButtonsDialog(CadCatalog catalog, IEnumerable<string> selected, IReadOnlyList<CadSession> liveSessions)
    {
        original = catalog;
        var chosen = selected.ToHashSet(StringComparer.Ordinal);
        Text = "RoBIM Add-in Manager — CAD 버튼 선택";
        Font = new Font("맑은 고딕", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1060, 650); MinimumSize = new Size(950, 560);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new(18) };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Padding = new(0, 0, 0, 12),
            Text = "필요한 버튼만 선택하세요. 버튼 이름·패널·순서를 수정할 수 있습니다.\n적용 준비 후 AutoCAD → RoBIM → 관리 → Reload를 누르면 반영됩니다." });
        var sessionRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new(0, 0, 0, 10) };
        sessionRow.Controls.Add(new Label { Text = "적용할 CAD", AutoSize = true, Padding = new(0, 7, 10, 0) }); sessionRow.Controls.Add(sessions);
        if (liveSessions.Count == 0) sessions.Items.Add(new SessionItem(null, "CAD 종료됨 — 다음 실행 후 Reload로 적용"));
        else { sessions.Items.Add(new SessionItem(null,"실행 중인 AutoCAD 전체 — 각 창에서 Reload")); foreach(var session in liveSessions) sessions.Items.Add(new SessionItem(session,$"PID {session.Pid} · {session.Title}")); }
        sessions.SelectedIndex = 0; layout.Controls.Add(sessionRow);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "등록", FillWeight = 35 });
        foreach (var item in new[] { ("Module", "기능", 100, true), ("Title", "버튼 이름", 180, false), ("Panel", "패널", 90, false), ("Order", "순서", 45, false) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = item.Item1, HeaderText = item.Item2, FillWeight = item.Item3, ReadOnly = item.Item4 });
        foreach (var module in catalog.Modules)
        foreach (var action in module.Actions)
        {
            var key = CadFiles.Key(module, action);
            var row = grid.Rows.Add(chosen.Contains(key), module.Name, action.Title, action.Panel, action.Order);
            grid.Rows[row].Tag = key;
            grid.Rows[row].Cells["Title"].ToolTipText = action.Description;
        }
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        layout.Controls.Add(grid);
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new(0, 10, 0, 10),
            Text = "같은 기능의 여러 버튼은 하나의 DLL로 Reload됩니다. 체크를 해제하면 리본과 직접 명령 실행에서도 해제됩니다.\n준비 단계에서는 설치 파일을 교체하지 않습니다. 기존 구성은 CAD에서 적용할 때까지 유지됩니다." });
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true, Height = 36 };
        var apply = new Button { Text = "선택 적용 준비", AutoSize = true, Height = 36, BackColor = Color.FromArgb(30, 91, 166), ForeColor = Color.White };
        buttons.Controls.AddRange([cancel, apply]); layout.Controls.Add(buttons); Controls.Add(layout); CancelButton = cancel;
        apply.Click += (_, _) =>
        {
            try
            {
                grid.EndEdit(); var config = Configuration;
                if (config.Modules.SelectMany(m => m.Actions).Any(a => string.IsNullOrWhiteSpace(a.Title) || string.IsNullOrWhiteSpace(a.Panel)))
                    throw new InvalidOperationException("버튼 이름과 패널을 입력하십시오.");
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, "버튼 설정을 확인하십시오. " + ex.Message, Text); }
        };
    }
}
