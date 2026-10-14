using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenStudio.Controls;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio;

public partial class MainWindow : Window
{
    public EditingSession Editing { get; private set; }
    public StudioPreferences Preferences { get; }
    private VideoEngine? engine;
    private MultitrackEngine? compositor;
    private PreviewRenderer? preview;
    private ScreenCaptureSession? recorder;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch playbackClock = new();
    private double playbackOrigin, position, previewZoom = 1;
    private bool playing, recording, busy, dirty, suppressSeek, closeAfterRecord;
    private string? projectPath;
    private readonly string? preferencesPath;
    private TimelineClip? clipboard;
    private CancellationTokenSource? recordStart, exporting;
    private HwndSource? windowSource;
    private bool hotkey;
    private WindowState previousState;
    private readonly Dictionary<string, List<Button>> buttons = [];
    private readonly Dictionary<string, List<MenuItem>> menuCommands = [];
    private readonly List<(bool Left, TabControl Tabs)> sideTabs = [];
    private readonly HashSet<string> posterWarnings = [];
    private Button? playButton, recordButton;
    private DateTime noticeUntil;
    private const int HotkeyId = 9001;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public MainWindow() : this(null) { }
    public MainWindow(StudioPreferences? preferences, string? preferencesPath = null)
    {
        this.preferencesPath = preferencesPath;
        Preferences = preferences ?? StudioPreferences.Load();
        StudioUi.ApplyTheme(Preferences.Dark);
        Editing = new EditingSession(new StudioProject { Output = CloneOutput(Preferences.Defaults) });
        InitializeComponent();
        try
        {
            engine = new VideoEngine(); compositor = new MultitrackEngine(engine); recorder = new ScreenCaptureSession(engine); preview = new PreviewRenderer(Stage, AudioHost, engine);
            preview.PlaybackError += (_, message) => ShowNotice(message);
        }
        catch (Exception e) { ShowNotice(e.Message); }
        InitializePreviewEditing(); BuildMenus(); BuildToolbars(); BuildSidebars();
        Timeline.Session = Editing;
        if (engine != null) { var ffmpeg = engine.FfmpegPath; Timeline.WaveformLoader = path => Services.Waveform.PeaksAsync(ffmpeg, path, 400); }
        Timeline.SelectionChanged += (_, _) => { RefreshSidebars(); UpdateCommands(); };
        Timeline.EffectEditRequested += (_,edit)=>EditTimelineEffect(edit);
        Timeline.EffectSelected += (_,_)=>AddPanel(false,Timeline.SelectedEffect?.Kind.StartsWith("transition")==true?"Transiciones":"Movimiento");
        Timeline.PositionChanged += (_, _) => { if (!suppressSeek) Seek(Timeline.Position); };
        Timeline.EditRequested += async (_, edit) =>
        {
            if (busy) return;
            try
            {
                Pause();
                if (edit.Operation == "move") Editing.Move(edit.ClipId, edit.TrackId, edit.Start);
                else if (edit.Operation == "trim") Editing.Trim(edit.ClipId, edit.Start, edit.Duration, edit.Offset);
                else Editing.Split(edit.ClipId, edit.Start);
                dirty = true; await RefreshAsync();
            }
            catch (Exception e) { ShowNotice(e.Message); await RefreshAsync(); }
        };
        Timeline.FilesDropped += async (_, drop) => await ImportFilesAsync(drop.Paths, drop.TrackId, drop.Time);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle; windowSource = HwndSource.FromHwnd(handle); windowSource?.AddHook(WindowMessage);
            NativeCaption.Apply(this,Preferences.Dark);
            hotkey = RegisterHotKey(handle, HotkeyId, 0x4000 | 0x0002 | 0x0004, 0x78);
        };
        Closed += (_, _) =>
        {
            timer.Stop(); preview?.Dispose(); recorder?.Dispose(); captureSources?.Dispose(); DisposeAudioNotifications(); StopCaptureHelpers();
            if (hotkey) UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
            windowSource?.RemoveHook(WindowMessage);
        };
        timer.Tick += Timer_Tick; timer.Start(); UpdateCommands();
        if (preferencesPath == null)
        {
            var autosave = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            autosave.Tick += async (_,_) => await AutosaveNow(); autosave.Start();
            Loaded += async (_,_) =>
            {
                try
                {
                    if (File.Exists(AutosavePath()) && StudioDialogs.Confirm(this,"Hay un autoguardado de una sesión anterior. ¿Recuperarlo?"))
                    {
                        await OpenFileAsync(AutosavePath());
                        projectPath = null; dirty = true; UpdateTitle();
                    }
                }
                catch (Exception e) { ShowNotice(e.Message); }
            };
        }
    }
    private static OutputSettings CloneOutput(OutputSettings output) => JsonSerializer.Deserialize<OutputSettings>(JsonSerializer.Serialize(output))!;
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            if (recordStart != null) recordStart.Cancel(); else if (recording) Dispatcher.InvokeAsync(async () => await StopRecordingAsync());
        }
        return IntPtr.Zero;
    }
    private void ShowNotice(string text) { NoticeText.Text = text; Notice.Visibility = Visibility.Visible; noticeUntil = DateTime.UtcNow.AddSeconds(8); }
    private void RequireEngine() { if (engine == null || compositor == null) throw new InvalidOperationException("Instala FFmpeg y ffprobe o añádelos a la carpeta tools de la aplicación."); }
    private bool ConfirmDiscard() => !dirty || StudioDialogs.Confirm(this, "El proyecto tiene cambios sin guardar. ¿Descartarlos y continuar?");
    private void UpdateTitle() => Title = Editing.Project.Name + (dirty ? " *" : "") + " — Mambo Studio";
    public async Task RefreshAsync()
    {
        PreviewTransform.CancelGesture();
        var wasBusy = busy; busy = true; UpdateCommands();
        try
        {
            Timeline.Session = Editing;
            if (Timeline.SelectedTrackId == null || Editing.Track(Timeline.SelectedTrackId.Value) == null) Timeline.SelectedTrackId = Editing.Project.Tracks.FirstOrDefault()?.Id;
            if (Timeline.SelectedClipId.HasValue && Editing.Clip(Timeline.SelectedClipId.Value) == null) Timeline.SelectedClipId = null;
            if (preview != null)
            {
                var failed = await preview.BuildAsync(Editing.Project); Timeline.Thumbnails = preview.Thumbnails;
                var unseen = failed.Where(f => posterWarnings.Add(f.Path)).Take(2).ToArray();
                if (unseen.Length > 0) ShowNotice("Sin vista previa de " + string.Join(", ",unseen.Select(f => Path.GetFileName(f.Path))) + (string.IsNullOrWhiteSpace(unseen[0].Error) ? ". Archivo dañado o no disponible." : ": " + unseen[0].Error));
            }
            EmptyHint.Visibility = Editing.Project.Clips.Any() ? Visibility.Collapsed : Visibility.Visible;
            Seek(position); Timeline.Refresh(); RefreshSidebars(); UpdateTitle();
        }
        catch (Exception e) { ShowNotice("No se pudo actualizar la vista previa: " + e.Message); }
        finally { busy = wasBusy; UpdateCommands(); }
    }
    private async Task ChangeAsync(Action<StudioProject> action) { Pause(); Editing.Change(action); dirty = true; await RefreshAsync(); }
    public async Task OpenFileAsync(string path)
    {
        if (busy) return;
        if (Path.GetExtension(path).ToLowerInvariant() is not (".studio" or ".screenstudio")) { await ImportFilesAsync([path]); return; }
        if (!ConfirmDiscard()) return;
        Pause(); busy = true; UpdateCommands();
        try
        {
            RequireEngine(); var loaded = await StudioProject.LoadAsync(path); var relocated = false;
            var originals = loaded.Clips.Where(c => c.Kind != ClipKind.Text).Select(c => c.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var original in originals)
            {
                var storedCursor = loaded.Library.FirstOrDefault(a => a.Path == original)?.Cursor;
                var source = original;
                if (!File.Exists(source))
                {
                    var locate = new OpenFileDialog { Title = "Localizar " + Path.GetFileName(source) };
                    if (locate.ShowDialog(this) != true) return;
                    source = locate.FileName; relocated = true; foreach (var c in loaded.Clips.Where(c => c.SourcePath == original)) c.SourcePath = source;
                    loaded.Library.RemoveAll(a => a.Path == original);
                }
                var asset = await compositor!.InspectAsync(source); asset.Cursor ??= storedCursor;
                loaded.Library.RemoveAll(a => a.Path == source); loaded.Library.Add(asset);
                foreach (var c in loaded.Clips.Where(c => c.SourcePath == source && c.Kind is ClipKind.Video or ClipKind.Audio)) c.SourceDuration = asset.Duration;
                if (!string.IsNullOrEmpty(loaded.SourcePath) && asset.HasAudio && !loaded.Clips.Any(c => c.Kind == ClipKind.Audio))
                {
                    var video = loaded.Clips.First(c => c.Kind == ClipKind.Video);
                    var audio = video.Clone(true); audio.Kind = ClipKind.Audio; audio.Name += " · audio";
                    var track = loaded.Tracks.First(t => t.Kind == TrackKind.Audio); track.Clips.Add(audio);
                }
            }
            loaded.SourcePath = ""; loaded.Validate(); Editing = new EditingSession(loaded); projectPath = Path.GetFullPath(path); dirty = relocated; position = 0;
        }
        catch (Exception e) { ShowNotice(e.Message); }
        finally { busy = false; await RefreshAsync(); }
    }
    public async Task ImportFilesAsync(string[] paths, Guid? targetTrack = null, double? at = null, bool libraryOnly = false)
    {
        if (busy) return;
        Pause(); busy = true; UpdateCommands();
        try
        {
            RequireEngine(); var cursor = at ?? position;
            var transparent = 0;
            foreach (var path in paths)
            {
                var asset = await compositor!.InspectAsync(path);
                if (libraryOnly) Editing.Change(p => { if (!p.Library.Any(a => a.Path == asset.Path)) p.Library.Add(asset); });
                else { var clip = AddAsset(asset, targetTrack, cursor); cursor = clip.End; if (asset.Kind == ClipKind.Image && asset.HasAlpha) transparent++; }
                dirty = true;
            }
            if (transparent > 0) ShowNotice("La imagen tiene transparencia: las zonas transparentes muestran el fondo del lienzo.");
        }
        catch (Exception e) { ShowNotice(e.Message); }
        finally { busy = false; await RefreshAsync(); }
    }
    private TimelineClip AddAsset(MediaAsset asset, Guid? target, double at)
    {
        TimelineClip? clip = null;
        Editing.Change(project =>
        {
            var category = asset.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Visual;
            var track = target.HasValue ? Editing.Track(target.Value) : Timeline.SelectedTrackId.HasValue ? Editing.Track(Timeline.SelectedTrackId.Value) : null;
            if (target.HasValue && track?.Kind != category) throw new InvalidOperationException("El archivo requiere una pista de " + (category == TrackKind.Audio ? "audio." : "vídeo, imagen o texto."));
            if (track?.Kind != category) track = project.Tracks.FirstOrDefault(t => t.Kind == category);
            if (track == null) { track = new StudioTrack { Kind = category, Name = category == TrackKind.Audio ? "Audio 1" : "Visual 1" }; project.Tracks.Add(track); }
            var start = EditingSession.NextFree(track, at, asset.Duration);
            clip = new TimelineClip { Kind = asset.Kind, SourcePath = asset.Path, Name = asset.Name, Start = start, Duration = asset.Duration, SourceDuration = asset.Duration };
            if (asset.Cursor != null) { clip.Effects.Follow = FollowMode.Clicks; clip.Effects.ShowCursor = asset.Cursor.ShowCursor; }
            track.Clips.Add(clip); Timeline.SelectedTrackId = track.Id; Timeline.SelectedClipId = clip.Id;
            if (!project.Library.Any(a => string.Equals(a.Path, asset.Path, StringComparison.OrdinalIgnoreCase))) project.Library.Add(asset);
            if (asset.Kind == ClipKind.Video && asset.HasAudio)
            {
                var audio = clip.Clone(true); audio.Kind = ClipKind.Audio; audio.Name = asset.Name + " · audio";
                var audioTrack = project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && Editing.CanPlace(t.Id, audio, start));
                if (audioTrack == null) { audioTrack = new StudioTrack { Kind = TrackKind.Audio, Name = "Audio " + (project.Tracks.Count(t => t.Kind == TrackKind.Audio) + 1) }; project.Tracks.Add(audioTrack); }
                audioTrack.Clips.Add(audio);
            }
        });
        return clip!;
    }
    private async Task SaveAsync(bool saveAs = false)
    {
        var path = projectPath;
        if (path == null || saveAs)
        {
            var dialog = new SaveFileDialog { Filter = "Proyecto Studio|*.studio", FileName = Editing.Project.Name + ".studio" };
            if (dialog.ShowDialog(this) != true) return; path = dialog.FileName;
        }
        busy = true; UpdateCommands();
        var oldName = Editing.Project.Name; Editing.Project.Name = Path.GetFileNameWithoutExtension(path);
        try { await Editing.Project.SaveAsync(path); projectPath = path; dirty = false; UpdateTitle(); ClearAutosave(); }
        catch { Editing.Project.Name = oldName; throw; }
        finally { busy = false; UpdateCommands(); }
    }
    private void Pause() { playing = false; playbackClock.Stop(); preview?.Update(position, false); UpdateMediaButtons(); }
    public void Seek(double time)
    {
        position = Math.Clamp(time, 0, Math.Max(0, Editing.Project.Duration - .00001));
        playbackOrigin = position; if (playing) playbackClock.Restart();
        suppressSeek = true; Timeline.Position = position; suppressSeek = false;
        preview?.Update(position, playing, true); UpdateTimecode();
        PreviewTransform.InvalidateVisual();
    }
    private void UpdateTimecode()
    {
        var time = TimeSpan.FromSeconds(recording ? recorder!.EncodedDuration.TotalSeconds : position);
        Timecode.Text = $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (recording) { UpdateTimecode(); if (recorder?.IsRecording == false) _ = StopRecordingAsync(); }
        else if (playing)
        {
            var now = playbackOrigin + playbackClock.Elapsed.TotalSeconds;
            if (now >= Editing.Project.Duration) { Pause(); Seek(Editing.Project.Duration - 1d / Editing.Project.Output.FrameRate); }
            else { position = now; suppressSeek = true; Timeline.Position = position; suppressSeek = false; preview?.Update(position, true); PreviewTransform.InvalidateVisual(); UpdateTimecode(); }
        }
        if (Notice.Visibility == Visibility.Visible && DateTime.UtcNow > noticeUntil && recordStart == null) Notice.Visibility = Visibility.Collapsed;
    }
    private async Task RecordAsync()
    {
        if (recordStart != null) { recordStart.Cancel(); return; }
        if (recording) { await StopRecordingAsync(); return; }
        RequireEngine(); Pause(); busy = true; previousState = WindowState; recordStart = new CancellationTokenSource(); UpdateCommands();
        recordingInsertTime = position;
        try
        {
            for (var i = 3; i > 0; i--) { ShowNotice($"Grabación en {i}… Pulsa el icono de grabación para cancelar."); await Task.Delay(1000, recordStart.Token); }
            WindowState = WindowState.Minimized; await Task.Delay(300, recordStart.Token);
            await recorder!.StartAsync(includeCursor, cancellation: recordStart.Token, frameRate: Editing.Project.Output.FrameRate, options: Preferences.Capture);
            captureSources = new CaptureSources(engine!);
            await captureSources.StartAsync(Preferences.Capture,recorder.OutputPath!,() => recorder.SessionTime,Editing.Project.Output.FrameRate,recordStart.Token);
            recording = true; Notice.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { captureSources?.Dispose(); captureSources = null; recorder?.Dispose(); WindowState = previousState; busy = false; ShowNotice("Grabación cancelada."); }
        catch (Exception e) { captureSources?.Dispose(); captureSources = null; if (recorder?.IsRecording == true) { try { await recorder.StopAsync(); } catch { recorder.Dispose(); } } WindowState = previousState; busy = false; ShowNotice(e.Message); }
        finally { recordStart.Dispose(); recordStart = null; UpdateCommands(); if (closeAfterRecord && !recording) { closeAfterRecord = false; Close(); } }
    }
    private async Task StopRecordingAsync()
    {
        if (!recording) return; recording = false; UpdateCommands();
        try
        {
            var path = await recorder!.StopAsync(); var extras = captureSources == null ? [] : await captureSources.StopAsync();
            var warning = captureSources?.LastError; captureSources?.Dispose(); captureSources = null;
            WindowState = previousState; Activate(); await ImportCaptureAsync(path,extras);
            if (warning != null) ShowNotice(warning);
        }
        catch (Exception e) { WindowState = previousState; ShowNotice(e.Message); }
        finally { captureSources?.Dispose(); captureSources = null; busy = false; UpdateCommands(); }
        if (closeAfterRecord) { closeAfterRecord = false; Close(); }
    }
    private async Task ExportAsync()
    {
        RequireEngine(); Editing.Project.Validate();
        var format = Editing.Project.Output.Format;
        var dialog = new SaveFileDialog { Filter = $"Vídeo {format.ToUpperInvariant()}|*.{format}", FileName = Editing.Project.Name + "." + format };
        if (dialog.ShowDialog(this) != true) return;
        if (!string.Equals(Path.GetExtension(dialog.FileName), "." + format, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La extensión debe coincidir con el formato elegido en Configuración.");
        Pause(); busy = true; exporting = new CancellationTokenSource(); UpdateCommands(); ExportOverlay.Visibility = Visibility.Visible;
        try
        {
            var snapshot = Editing.Project.Clone();
            await compositor!.ExportAsync(snapshot, dialog.FileName, new Progress<double>(v => { ExportProgress.Value = v * 100; ExportLabel.Text = $"Exportando {v:P0}"; }), exporting.Token);
            ShowNotice("Exportación completada: " + dialog.FileName);
        }
        catch (OperationCanceledException) { ShowNotice("Exportación cancelada."); }
        finally { exporting.Dispose(); exporting = null; busy = false; ExportOverlay.Visibility = Visibility.Collapsed; UpdateCommands(); }
    }
    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => ResizePreview();
    private void ResizePreview()
    {
        if (PreviewBox == null || PreviewScroll == null) return;
        PreviewScroll.HorizontalScrollBarVisibility = previewZoom <= 1 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PreviewScroll.VerticalScrollBarVisibility = previewZoom <= 1 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PreviewBox.Width = Math.Max(100, PreviewScroll.ActualWidth - 24) * previewZoom; PreviewBox.Height = Math.Max(100, PreviewScroll.ActualHeight - 24) * previewZoom;
    }
    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) { Timeline.ViewportWidth = Math.Max(300, TimelineScroll.ActualWidth - 18); Timeline.Refresh(); }
    private void Window_Drop(object sender, DragEventArgs e) { if (!e.Handled && e.Data.GetData(DataFormats.FileDrop) is string[] paths) { e.Handled = true; _ = paths.Length == 1 && Path.GetExtension(paths[0]) is ".studio" or ".screenstudio" ? OpenFileAsync(paths[0]) : ImportFilesAsync(paths); } }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (recordStart != null) { e.Cancel = true; closeAfterRecord = true; recordStart.Cancel(); return; }
        if (recording) { e.Cancel = true; closeAfterRecord = true; _ = StopRecordingAsync(); return; }
        if (busy) { e.Cancel = true; ShowNotice("Espera a que termine la operación o cancela la exportación."); return; }
        if (!ConfirmDiscard()) e.Cancel = true;
        else ClearAutosave();
    }
    public static string AutosavePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenStudio","autosave.studio");
    public static void ClearAutosave() { try { File.Delete(AutosavePath()); } catch { } }
    private async Task AutosaveNow()
    {
        if (busy || recording || recordStart != null || exporting != null || !dirty) return;
        try { await Editing.Project.SaveAsync(AutosavePath()); }
        catch { }
    }
}


