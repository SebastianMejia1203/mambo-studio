using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScreenStudio.Models;
namespace ScreenStudio.UI;

public static class StudioDialogs
{
    private static (Window Window, StackPanel Body, StackPanel Actions, TextBlock Error) Create(Window owner, string title, double width = 440)
    {
        var window = new Window { Owner = owner.IsVisible ? owner : null, Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, ShowInTaskbar = false };
        var body = new StackPanel { Margin = new Thickness(22) }; window.Content = body;
        var error = StudioUi.Label("", "Danger", 11); error.Margin = new Thickness(0, 8, 0, 8);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        return (window, body, actions, error);
    }
    public static bool Confirm(Window owner, string message)
    {
        var (window, body, actions, _) = Create(owner, "Cambios sin guardar");
        body.Children.Add(StudioUi.Label(message));
        actions.Margin = new Thickness(0, 16, 0, 0);
        actions.Children.Add(StudioUi.Button("check", "Descartar cambios y continuar", () => window.DialogResult = true));
        actions.Children.Add(StudioUi.Button("close", "Cancelar", () => window.DialogResult = false)); body.Children.Add(actions);
        return window.ShowDialog() == true;
    }
    public static string? Text(Window owner, string initial = "Texto")
    {
        var (window, body, actions, _) = Create(owner, "Texto");
        var input = new TextBox { Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110 };
        StudioUi.Field(body, "Contenido", input);
        actions.Children.Add(StudioUi.Button("check", "Insertar texto", () => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; }));
        actions.Children.Add(StudioUi.Button("close", "Cancelar", () => window.DialogResult = false)); body.Children.Add(actions);
        return window.ShowDialog() == true ? input.Text : null;
    }
    public static OutputSettings? Settings(Window owner, OutputSettings initial)
    {
        var (window, body, actions, error) = Create(owner, "Configuración", 460);
        var fps = new ComboBox { ItemsSource = new[] { 24, 25, 30, 50, 60 }, SelectedItem = initial.FrameRate };
        var video = new ComboBox { ItemsSource = new[] { "H.264 · libx264", "H.265 · libx265", "VP9 · libvpx-vp9" }, SelectedIndex = Array.IndexOf(new[] { "libx264", "libx265", "libvpx-vp9" }, initial.VideoEncoder) };
        var audio = new ComboBox { ItemsSource = new[] { "AAC", "Opus", "PCM · 16 bits" }, SelectedIndex = Array.IndexOf(new[] { "aac", "libopus", "pcm_s16le" }, initial.AudioEncoder) };
        var format = new ComboBox { ItemsSource = new[] { "MP4", "MKV", "WebM", "MOV" }, SelectedIndex = Array.IndexOf(new[] { "mp4", "mkv", "webm", "mov" }, initial.Format) };
        StudioUi.Field(body, "Fotogramas por segundo · captura y exportación", fps); StudioUi.Field(body, "Codificador de vídeo", video); StudioUi.Field(body, "Codificador de audio", audio); StudioUi.Field(body, "Formato de salida", format);
        var subtitles = new CheckBox { Content = "Generar subtítulos .srt desde los textos", IsChecked = initial.Subtitles, Margin = new Thickness(0,8,0,0) }; body.Children.Add(subtitles);
        var stabilize = new CheckBox { Content = "Estabilizar el vídeo al exportar (más lento)", IsChecked = initial.Stabilize, Margin = new Thickness(0,0,0,0) }; body.Children.Add(stabilize);
        body.Children.Add(StudioUi.Label("Se aplican al proyecto actual y como valores predeterminados de los nuevos proyectos.", "Muted", 11));
        OutputSettings? result = null;
        actions.Children.Add(StudioUi.Button("check", "Guardar configuración", () =>
        {
            try
            {
                result = new OutputSettings { FrameRate = (int)fps.SelectedItem, VideoEncoder = new[] { "libx264", "libx265", "libvpx-vp9" }[video.SelectedIndex], AudioEncoder = new[] { "aac", "libopus", "pcm_s16le" }[audio.SelectedIndex], Format = new[] { "mp4", "mkv", "webm", "mov" }[format.SelectedIndex], Subtitles = subtitles.IsChecked == true, Stabilize = stabilize.IsChecked == true };
                result.Validate(); window.DialogResult = true;
            }
            catch (Exception e) { error.Text = e.Message; }
        }));
        actions.Children.Add(StudioUi.Button("close", "Cancelar", () => window.DialogResult = false)); body.Children.Add(error); body.Children.Add(actions);
        return window.ShowDialog() == true ? result : null;
    }
    public static double Number(TextBox input)
    {
        if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value)) return value;
        if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value)) return value;
        throw new InvalidOperationException("Introduce un valor numérico válido.");
    }
}
