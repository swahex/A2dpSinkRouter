namespace A2dpSinkRouter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--render-language-preview")
        {
            using var language = new UI.LanguageDialog { Opacity = 0, ShowInTaskbar = false };
            language.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(language.Width, language.Height);
            language.DrawToBitmap(bitmap, new Rectangle(Point.Empty, language.Size));
            bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (args.Length >= 2 && args[0] == "--render-preview")
        {
            string language = args.Length > 2 ? args[2] : "zh-CN";
            int width = args.Length > 3 ? int.Parse(args[3]) : 1120;
            int height = args.Length > 4 ? int.Parse(args[4]) : 880;
            using var preview = new MainForm(true, language) { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new(-20000, -20000), Size = new(width, height) };
            preview.Show(); Application.DoEvents();
            if (args.Contains("--dark")) preview.ChangeAppearance(true);
            if (args.Contains("--switch-theme")) { preview.ChangeAppearance(!UI.Theme.Dark); preview.ChangeAppearance(!UI.Theme.Dark); }
            if (args.Contains("--switch-language")) preview.SwitchPreviewLanguage(language == "en-US" ? "zh-CN" : "en-US");
            if (args.Contains("--resize-check")) preview.CheckPreviewResize(Path.GetFullPath(args[1]) + ".resize.json");
            preview.RenderPreview(Path.GetFullPath(args[1]), args.Contains("--logs"), args.Contains("--bottom"));
            preview.DisposePreview();
            return;
        }
        try { Infrastructure.UiText.Language = Infrastructure.Settings.Load().Language; } catch { }
        using var singleton = new Mutex(true, @"Local\A2dpSinkRouter", out bool first);
        if (!first)
        {
            MessageBox.Show(Infrastructure.UiText.T("程序已在运行，请从系统托盘打开。", "The app is already running. Open it from the system tray."), "A2DP Sink Router");
            return;
        }
        if (!File.Exists(Path.Combine(Infrastructure.Settings.DirectoryPath, "settings.json")))
        {
            using var language = new UI.LanguageDialog();
            if (language.ShowDialog() != DialogResult.OK) return;
            var settings = new Infrastructure.Settings { Language = language.SelectedLanguage };
            try { settings.Save(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "A2DP Sink Router"); return; }
            Infrastructure.UiText.Language = settings.Language;
        }
        Application.ThreadException += (_, e) => MessageBox.Show(Infrastructure.UiText.Engine(e.Exception.Message), Infrastructure.UiText.T("操作失败", "Operation failed"));
        Application.Run(new MainForm());
    }
}
