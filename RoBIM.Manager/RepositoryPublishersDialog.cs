using RoBIM.Manager.Core;

namespace RoBIM.Manager;

internal sealed class RepositoryPublishersDialog : Form
{
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
    private readonly CheckBox account = new() { Text = "연결 계정이 접근할 수 있는 RoBIM 저장소도 검색", AutoSize = true };
    private readonly TextBox name = new() { Width = 240, PlaceholderText = "제작자 또는 조직 이름 / GitHub 주소" };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    public RepositorySearchSettings? Result { get; private set; }
    public RepositoryPublishersDialog(RepositorySearchSettings current)
    {
        Text = "제작자·조직 검색 대상"; Width = 760; Height = 480; MinimumSize = new(660, 400);
        StartPosition = FormStartPosition.CenterParent; Font = new("맑은 고딕", 9F); BackColor = Color.FromArgb(246, 247, 244);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new(SizeType.AutoSize)); Controls.Add(root);
        root.Controls.Add(new Label { Text = "여러 개인 제작자와 조직을 등록할 수 있습니다. 저장 후 도구 목록의 새로고침에서 검색합니다.", AutoSize = true, MaximumSize = new(580, 0), Margin = new(0, 0, 0, 12) });
        table.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "검색", FillWeight = 30 });
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "제작자·조직", ReadOnly = true, FillWeight = 140 });
        var types = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "종류", FillWeight = 65 };
        types.Items.AddRange("자동 확인", "개인", "조직"); table.Columns.Add(types); table.RowTemplate.Height = 32;
        root.Controls.Add(table);
        foreach (var item in current.Publishers) table.Rows.Add(item.Enabled, item.Name, KindLabel(item.Kind));
        var addRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new(0, 12, 0, 8) };
        kind.Items.AddRange(["자동 확인", "개인", "조직"]); kind.SelectedIndex = 0;
        var add = new Button { Text = "추가", AutoSize = true }; var remove = new Button { Text = "선택 삭제", AutoSize = true };
        addRow.Controls.AddRange([name, kind, add, remove]); root.Controls.Add(addRow);
        account.Checked = current.IncludeConnectedAccount; root.Controls.Add(account);
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new(0, 12, 0, 0) };
        var save = new Button { Text = "저장", AutoSize = true, BackColor = Color.FromArgb(36, 122, 113), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel }; bottom.Controls.AddRange([save, cancel]); root.Controls.Add(bottom);
        CancelButton = cancel;
        add.Click += (_, _) =>
        {
            try
            {
                var owner = RepositoryDiscovery.PublisherName(name.Text);
                if (table.Rows.Cast<DataGridViewRow>().Any(r => string.Equals(r.Cells["Name"].Value?.ToString(), owner, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("이미 등록된 검색 대상입니다.");
                table.Rows.Add(true, owner, kind.SelectedItem ?? "자동 확인"); name.Clear();
            }
            catch (ArgumentException error) { MessageBox.Show(this, error.Message, Text); }
        };
        remove.Click += (_, _) => { foreach (var row in table.SelectedRows.Cast<DataGridViewRow>().ToArray()) table.Rows.Remove(row); };
        save.Click += (_, _) =>
        {
            table.EndEdit();
            Result = new(1, account.Checked, table.Rows.Cast<DataGridViewRow>().Select(r => new RepositoryPublisher(
                r.Cells["Name"].Value!.ToString()!, KindValue(r.Cells["Kind"].Value?.ToString()), Convert.ToBoolean(r.Cells["Enabled"].Value))).ToArray());
            DialogResult = DialogResult.OK;
        };
    }
    private static string KindLabel(string kind) => kind == "organization" ? "조직" : kind == "user" ? "개인" : "자동 확인";
    private static string KindValue(string? kind) => kind == "조직" ? "organization" : kind == "개인" ? "user" : "auto";
}

public sealed partial class ManagerForm
{
    private void EditRepositoryPublishers()
    {
        if (busy || preview) return;
        try
        {
            var discovery = new RepositoryDiscovery(paths);
            using var dialog = new RepositoryPublishersDialog(discovery.Settings());
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
            discovery.SaveSettings(dialog.Result);
            footer.Text = "검색 대상 저장 완료 · 새로고침으로 새 도구를 확인하세요. 기존 설치와 리본은 유지됩니다.";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "검색 대상 설정"); }
    }
}
