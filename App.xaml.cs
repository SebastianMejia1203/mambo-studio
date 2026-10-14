using System.Windows;

namespace ScreenStudio;

public partial class App : Application
{
    public bool OpenMainWindowOnStartup { get; set; } = true;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OpenMainWindowOnStartup) return;
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Length == 1)
            window.Loaded += async (_, _) => await window.OpenFileAsync(e.Args[0]);
        window.Show();
    }
}

