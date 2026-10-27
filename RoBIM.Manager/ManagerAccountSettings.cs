using System.Text.Json;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Button accountSwitchButton = MakeButton("계정 변경");
    private readonly Button accountAdvancedButton = MakeButton("고급 설정");
    private readonly FlowLayoutPanel accountSwitchPanel = AccountSection();
    private readonly FlowLayoutPanel accountAdvancedPanel = AccountSection();

    private static FlowLayoutPanel AccountSection() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Visible = false, Margin = new(0, 0, 0, 14), Padding = new(12),
        BackColor = Color.FromArgb(239, 243, 249)
    };

    public void VerifyAccountSettingsUi(string destination)
    {
        Shown += (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
                void Capture(string suffix)
                {
                    PerformLayout();
                    using var bitmap = new Bitmap(Width, Height);
                    DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(destination + suffix + ".png");
                }
                SwitchPage("Account");
                accountStatus.Text = "연결된 GitHub ID  ·  example-user  /  브라우저 계정";
                Require(!accountSwitchPanel.Visible && !accountAdvancedPanel.Visible, "OnlyCurrentAccountAndPreferencesShownInitially");
                Require(!savedAccounts.Visible && !tokenInput.Visible, "AccountPickerAndTokenHiddenInitially");
                Capture("-basic");
                accountSwitchButton.PerformClick();
                Require(accountSwitchPanel.Visible && savedAccounts.Visible && !accountAdvancedPanel.Visible, "AccountChangeShowsPickerOnly");
                accountSwitchButton.PerformClick();
                Require(!accountSwitchPanel.Visible, "AccountChangeCanClose");
                accountAdvancedButton.PerformClick();
                Require(accountAdvancedPanel.Visible && tokenInput.Visible && !accountSwitchPanel.Visible, "AdvancedSettingsShowsTokenOnlyOnRequest");
                Capture("-advanced");
                tokenInput.Text = "fixture"; accountAdvancedButton.PerformClick();
                Require(!accountAdvancedPanel.Visible && tokenInput.Text.Length == 0, "ClosingAdvancedClearsTokenInput");
                File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed=true, Checks=checks }));
            }
            catch (Exception error) { File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed=false, Checks=checks, Error=error.ToString() })); }
            finally { Close(); }
        };
    }
}
