using System.Runtime.InteropServices;
using A2dpSinkRouter.UI;

namespace A2dpSinkRouter;

internal sealed partial class MainForm
{
    internal void ChangeAppearance(bool dark)
    {
        Theme.Dark = dark;
        _settings.Appearance = dark ? "dark" : "light";
        ApplyAppearance(); SaveSelections();
    }

    private void ApplyAppearance()
    {
        SuspendLayout();
        try
        {
            Theme.Apply(this); Theme.Apply(_trayMenu);
            _lightItem.Checked = !Theme.Dark; _darkItem.Checked = Theme.Dark;
            _stats.ForeColor = Theme.Ink;
            UpdateConnectionState(); UpdateStatus();
            if (_preview && IsHandleCreated) PopulatePreview();
            if (IsHandleCreated) ApplyCaptionTheme();
        }
        finally { ResumeLayout(true); Invalidate(true); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); ApplyCaptionTheme();
    }
    private void ApplyCaptionTheme()
    {
        int dark = Theme.Dark ? 1 : 0;
        // Older Windows 10 builds use attribute 19, newer builds use 20.
        if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
