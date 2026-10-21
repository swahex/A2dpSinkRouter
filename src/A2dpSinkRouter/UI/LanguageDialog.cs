namespace A2dpSinkRouter.UI;

internal sealed class LanguageDialog : Form
{
    internal string SelectedLanguage { get; private set; } = "zh-CN";
    internal LanguageDialog()
    {
        Text = "A2DP Sink Router · Language / 语言";
        AutoScaleDimensions = new(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(440, 240); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10); BackColor = Color.White; ForeColor = Color.FromArgb(30, 30, 30);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28), ColumnCount = 1, RowCount = 4 };
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        var title = new Label { Text = "选择语言 / Choose your language", AutoSize = true, Font = new("Segoe UI", 13, FontStyle.Bold), Margin = new(0, 0, 0, 12) };
        var hint = new Label { Text = "之后可在顶部菜单中更改。\nYou can change this later in the top menu.", AutoSize = true, Margin = new(0, 0, 0, 18) };
        var choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        foreach (var (text, language) in new[] { ("简体中文", "zh-CN"), ("English", "en-US") })
        {
            var button = new AppButton { Text = text, Width = 160, AutoSize = false };
            button.Click += (_, _) => { SelectedLanguage = language; DialogResult = DialogResult.OK; Close(); };
            choices.Controls.Add(button);
        }
        panel.Controls.Add(title, 0, 0); panel.Controls.Add(hint, 0, 1); panel.Controls.Add(choices, 0, 2); Controls.Add(panel);
        using var stream = typeof(LanguageDialog).Assembly.GetManifestResourceStream("App.Icon")!;
        using var icon = new Icon(stream); Icon = (Icon)icon.Clone();
    }
}
