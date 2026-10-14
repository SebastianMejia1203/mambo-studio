using System.IO;
using System.Text.Json;
namespace ScreenStudio.Models;

public sealed class StudioPreferences
{
    public bool Dark { get; set; } = true;
    public OutputSettings Defaults { get; set; } = new();
    public CaptureOptions Capture { get; set; } = new();
    public bool LeftVisible { get; set; } = true;
    public bool RightVisible { get; set; } = true;
    public bool LeftCollapsed { get; set; }
    public bool RightCollapsed { get; set; }
    public List<string> LeftTabs { get; set; } = ["Archivos"];
    public List<string> RightTabs { get; set; } = ["Inspector", "Lienzo"];
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenStudio", "preferences.json");
    public static StudioPreferences Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<StudioPreferences>(File.ReadAllText(path ?? DefaultPath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
