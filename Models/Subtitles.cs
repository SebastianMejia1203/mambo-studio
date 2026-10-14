using System.Globalization;
namespace ScreenStudio.Models;

// Subtítulos laterales (.srt) generados desde los clips de texto del proyecto.
public static class Subtitles
{
    public static List<(double Start, double End, string Text)> FromProject(StudioProject project) =>
        project.Clips.Where(c => c.Kind == ClipKind.Text && !string.IsNullOrWhiteSpace(c.Text))
            .OrderBy(c => c.Start).ThenBy(c => c.End)
            .Select(c => (c.Start, c.End, c.Text.Trim().Replace("\r\n", "\n"))).ToList();

    public static string Render(IEnumerable<(double Start, double End, string Text)> cues)
    {
        var lines = new List<string>(); var number = 1;
        foreach (var (start, end, text) in cues)
        {
            if (end <= start || string.IsNullOrWhiteSpace(text)) continue;
            lines.Add(number++.ToString());
            lines.Add($"{Stamp(start)} --> {Stamp(end)}");
            lines.Add(text);
            lines.Add("");
        }
        return string.Join("\r\n", lines);
    }
    private static string Stamp(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var total = (long)Math.Round(seconds * 1000);
        return $"{total / 3600000:00}:{total / 60000 % 60:00}:{total / 1000 % 60:00},{total % 1000:000}";
    }
}
