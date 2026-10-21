using System.Text.Json;

namespace A2dpSinkRouter.Infrastructure;

internal sealed class SavedRoute
{
    public string? InputId { get; set; }
    public string? OutputId { get; set; }
}

internal sealed class Settings
{
    public string? LastBluetoothId { get; set; }
    public string? InputId { get; set; }
    public string? OutputId { get; set; }
    public int BufferMs { get; set; } = 12;
    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }
    public bool ReconnectOnStart { get; set; }
    public bool ShowAllInputs { get; set; }
    public bool ExclusiveOutput { get; set; }
    public string Language { get; set; } = "zh-CN";
    public string Appearance { get; set; } = "light";
    public bool CloseToTray { get; set; } = true;
    public Dictionary<string, SavedRoute> Routes { get; set; } = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2dpSinkRouter");
    public static Settings Load()
    {
        string path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
        settings.BufferMs = Math.Clamp(settings.BufferMs, 5, 100);
        settings.Volume = Math.Clamp(settings.Volume, 0, 100);
        settings.Routes ??= new();
        if (settings.Language is not ("zh-CN" or "en-US")) settings.Language = "zh-CN";
        if (settings.Appearance is not ("light" or "dark")) settings.Appearance = "light";
        return settings;
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, "settings.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
