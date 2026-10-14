using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio;
public partial class MainWindow
{
    private CaptureSources? captureSources;
    private List<AudioDevice> microphones = [], outputs = [];
    private List<string> cameras = [];
    private bool devicesLoaded;
    private bool cameraLoading;
    private MMDeviceEnumerator? audioWatcher;
    private IMMNotificationClient? audioClient;
    private bool audioRefreshScheduled;
    private ComboBox? micCombo, outputCombo, cameraCombo;
    private ComboBox? captureModes, captureFirst, captureSecond, captureLayout, captureWindow, captureMic, captureOutput, captureCamera;
    private CheckBox? captureMicrophone, captureSystem, captureReverse;
    private TextBox? captureX, captureY, captureWidth, captureHeight;
    private TextBlock? captureRegion, previewStatus, deviceStatus;
    private Image? previewImage;
    private WasapiCapture? micMeterCap, sysMeterCap;
    private MMDevice? micMeterDevice, sysMeterDevice;
    private Border? micLevelFill, sysLevelFill;
    private string? lastAudioError;
    private sealed class AudioDeviceWatcher(MainWindow owner) : IMMNotificationClient
    {
        public void OnDeviceAdded(string deviceId) => owner.ScheduleAudioRefresh();
        public void OnDeviceRemoved(string deviceId) => owner.ScheduleAudioRefresh();
        public void OnDeviceStateChanged(string deviceId,DeviceState newState) => owner.ScheduleAudioRefresh();
        public void OnDefaultDeviceChanged(DataFlow flow,Role role,string defaultDeviceId) => owner.ScheduleAudioRefresh();
        public void OnPropertyValueChanged(string deviceId,PropertyKey key) { }
    }
    private void EnsureAudioNotifications()
    {
        if (audioWatcher != null) return;
        try { audioWatcher = new MMDeviceEnumerator(); audioClient = new AudioDeviceWatcher(this); audioWatcher.RegisterEndpointNotificationCallback(audioClient); }
        catch { audioWatcher?.Dispose(); audioWatcher = null; audioClient = null; }
    }
    private void DisposeAudioNotifications()
    {
        try { if (audioWatcher != null && audioClient != null) audioWatcher.UnregisterEndpointNotificationCallback(audioClient); } catch { }
        try { audioWatcher?.Dispose(); } catch { } finally { audioWatcher = null; audioClient = null; }
    }
    private void ScheduleAudioRefresh()
    {
        if (audioRefreshScheduled) return;
        audioRefreshScheduled = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(400);
            await Dispatcher.InvokeAsync(() =>
            {
                audioRefreshScheduled = false;
                try
                {
                    microphones = CaptureSources.AudioDevices(true); outputs = CaptureSources.AudioDevices(false);
                    if (System.Windows.Input.Keyboard.FocusedElement is ComboBox or System.Windows.Controls.Primitives.TextBoxBase) RefreshAudioSelectors();
                    else RefreshSidebars();
                    UpdateCommands();
                }
                catch (Exception e) { ShowNotice("No se pudieron actualizar los dispositivos de audio: " + e.Message); }
            });
        });
    }
    private void RefreshAudioSelectors()
    {
        if (micCombo is not { IsLoaded: true } mic || outputCombo is not { IsLoaded: true } output) { RefreshSidebars(); return; }
        var micId = mic.SelectedValue as string; mic.ItemsSource = SavedDevices(microphones,micId,"Dispositivo predeterminado"); mic.SelectedValue = micId ?? "";
        var outId = output.SelectedValue as string; output.ItemsSource = SavedDevices(outputs,outId,"Salida predeterminada"); output.SelectedValue = outId ?? "";
        lastAudioError = null; UpdateDeviceStatus();
    }
    private void RefreshCameraSelector()
    {
        if (cameraCombo is { IsLoaded: true } combo)
        {
            var current = combo.SelectedItem as string;
            var names = cameras.Concat(current is null or "Sin cámara" ? [] : new[] { current }).Distinct().ToArray();
            combo.ItemsSource = new[] { "Sin cámara" }.Concat(names);
            combo.SelectedItem = current != null && names.Contains(current) ? current : "Sin cámara";
        }
        else RefreshSidebars();
        UpdateDeviceStatus();
    }
    private void UpdateDeviceStatus()
    {
        if (deviceStatus is not { IsLoaded: true } status) return;
        string? text = null; string brush = "Muted";
        if (lastAudioError != null) { text = "No se pudieron enumerar los dispositivos de audio: " + lastAudioError; brush = "Danger"; }
        else if (microphones.Count == 0 && outputs.Count == 0) text = "No se encontraron micrófonos ni salidas de audio activas.";
        else if (cameraLoading) text = "Buscando cámara…";
        else if (engine == null && cameras.Count == 0) text = "Cámara no disponible: falta FFmpeg.";
        if (text == null) status.Visibility = Visibility.Collapsed;
        else { status.Text = text; status.SetResourceReference(TextBlock.ForegroundProperty,brush); status.Visibility = Visibility.Visible; }
    }
    private static IEnumerable<AudioDevice> SavedDevices(List<AudioDevice> devices,string? saved,string defaultName)
    {
        var result = new List<AudioDevice> { new("",defaultName) }; result.AddRange(devices);
        if (!string.IsNullOrEmpty(saved) && !devices.Any(d => d.Id == saved)) result.Add(new(saved,"Dispositivo guardado · no detectado ahora mismo"));
        return result;
    }
    private CaptureOptions ReadCaptureOptions()
    {
        var selected = new CaptureOptions { Mode=(CaptureMode)captureModes!.SelectedIndex, WindowHandle=(captureWindow!.SelectedItem as CaptureWindow)?.Handle ?? 0, X=checked((int)StudioDialogs.Number(captureX!)), Y=checked((int)StudioDialogs.Number(captureY!)), Width=checked((int)StudioDialogs.Number(captureWidth!)), Height=checked((int)StudioDialogs.Number(captureHeight!)), Microphone=captureMicrophone!.IsChecked == true, MicrophoneId=captureMic!.SelectedValue as string, SystemAudio=captureSystem!.IsChecked == true, OutputDeviceId=captureOutput!.SelectedValue as string, CameraDevice=captureCamera!.SelectedIndex > 0 ? captureCamera.SelectedItem as string : null, TrackCursor=true, AutoZoom=Preferences.Capture.AutoZoom };
        selected.MonitorDevice=(captureFirst!.SelectedItem as CaptureMonitor)?.Device; selected.SecondMonitorDevice=(captureSecond!.SelectedItem as CaptureMonitor)?.Device;
        selected.ScreenLayout=(ScreenLayout)captureLayout!.SelectedIndex; selected.ReverseScreens=captureReverse!.IsChecked==true;
        CaptureTargets.Bounds(selected); return selected;
    }
    private void SaveCaptureOptions()
    {
        if (busy) return;
        try
        {
            var selected = ReadCaptureOptions();
            Preferences.Capture = selected; SavePreferences();
            if (captureRegion != null) captureRegion.Text = $"{selected.Width} × {selected.Height} · {selected.X}, {selected.Y}";
        }
        catch (Exception e) { ShowNotice(e.Message); }
    }
    private static StackPanel MeterRow(Panel parent,string name,out Border fill)
    {
        var row = new StackPanel { Margin = new Thickness(0,0,0,14) };
        var label = StudioUi.Label("Nivel · " + name,"Muted",11); label.Margin = new Thickness(0,0,0,5); row.Children.Add(label);
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3) }; track.SetResourceReference(Border.BackgroundProperty,"Raised");
        fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 }; fill.SetResourceReference(Border.BackgroundProperty,"Accent");
        var host = new Grid(); host.Children.Add(track); host.Children.Add(fill); row.Children.Add(host);
        parent.Children.Add(row); return row;
    }
    private void StopCaptureHelpers()
    {
        StopSourcePreview();
        StopAudioMeters();
    }
    private void StopAudioMeters()
    {
        foreach (var cap in new[] { micMeterCap, sysMeterCap }) { try { cap?.StopRecording(); } catch { } try { cap?.Dispose(); } catch { } }
        micMeterCap = sysMeterCap = null;
        foreach (var device in new[] { micMeterDevice, sysMeterDevice }) { try { device?.Dispose(); } catch { } }
        micMeterDevice = sysMeterDevice = null;
    }
    private void UpdateAudioMeters()
    {
        StopAudioMeters();
        if (!IsVisible) return;
        try { if (captureMicrophone?.IsChecked == true) micMeterCap = StartMeter(captureMic?.SelectedValue as string,true,micLevelFill,out micMeterDevice); } catch { micMeterCap = null; }
        try { if (captureSystem?.IsChecked == true) sysMeterCap = StartMeter(captureOutput?.SelectedValue as string,false,sysLevelFill,out sysMeterDevice); } catch { sysMeterCap = null; }
    }
    private WasapiCapture? StartMeter(string? id,bool input,Border? fill,out MMDevice? device)
    {
        device = null;
        if (fill is not { IsLoaded: true } target) return null;
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            device = string.IsNullOrEmpty(id) ? enumerator.GetDefaultAudioEndpoint(input ? DataFlow.Capture : DataFlow.Render,Role.Multimedia) : enumerator.GetDevice(id);
            WasapiCapture cap = input ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
            var format = cap.WaveFormat;
            cap.DataAvailable += (_,e) =>
            {
                var level = Peak(e.Buffer,e.BytesRecorded,format);
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (!target.IsLoaded || target.Parent is not FrameworkElement host || host.ActualWidth <= 0) return;
                    target.Width = level < 0.01 ? 0 : Math.Max(2,host.ActualWidth * Math.Min(1,level));
                });
            };
            cap.StartRecording();
            return cap;
        }
        catch { try { device?.Dispose(); } catch { } device = null; throw; }
    }
    private static double Peak(byte[] buffer,int bytes,WaveFormat format)
    {
        try
        {
            double peak = 0;
            if (format.BitsPerSample == 32 && format.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (var i = 0; i + 4 <= bytes; i += 4) { var v = Math.Abs(BitConverter.ToSingle(buffer,i)); if (v > peak) peak = v; }
            }
            else if (format.BitsPerSample == 16)
            {
                for (var i = 0; i + 2 <= bytes; i += 2) { var v = Math.Abs(BitConverter.ToInt16(buffer,i) / 32768.0); if (v > peak) peak = v; }
            }
            return Math.Min(1,peak);
        }
        catch { return 0; }
    }
    private double recordingInsertTime;
    private void BuildCapture(StackPanel body)
    {
        StopCaptureHelpers();
        var options = Preferences.Capture;
        var modes = new ComboBox { ItemsSource = new[] { "Pantalla", "Región del escritorio", "Ventana", "Región de una ventana", "Dos pantallas" }, SelectedIndex = (int)options.Mode }; StudioUi.Field(body,"Origen",modes); captureModes = modes;
        var previewTitle = StudioUi.Label("Vista previa del origen · arrastra para fijar la región","Muted",11); previewTitle.Margin = new Thickness(0,0,0,5); body.Children.Add(previewTitle);
        var previewHost = new Grid { Height = 150, ClipToBounds = true };        previewImage = new Image { Stretch = Stretch.Uniform }; previewHost.Children.Add(previewImage);
        previewOverlay = new Canvas { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Cross };
        regionBand = new Rectangle { StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Opacity = 0.85, Visibility = Visibility.Collapsed };
        regionBand.SetResourceReference(Shape.StrokeProperty,"Accent"); regionBand.SetResourceReference(Shape.FillProperty,"AccentSoft");
        previewOverlay.Children.Add(regionBand); previewHost.Children.Add(previewOverlay); body.Children.Add(previewHost);
        previewOverlay.MouseLeftButtonDown += PreviewDragDown; previewOverlay.MouseMove += PreviewDragMove; previewOverlay.MouseLeftButtonUp += PreviewDragUp;
        previewStatus = StudioUi.Label("Selecciona un origen para verlo aquí.","Muted",11); previewStatus.Margin = new Thickness(0,5,0,14); body.Children.Add(previewStatus);
        previewImage.IsVisibleChanged += (s,_) => { if (s is Image img) { if (img.IsVisible) StartSourcePreview(); else StopSourcePreview(); } };
        var monitors=CaptureTargets.Monitors();
        var firstMonitor=new ComboBox { ItemsSource=monitors,DisplayMemberPath="Name",SelectedItem=monitors.FirstOrDefault(m=>m.Device==options.MonitorDevice) ?? monitors.FirstOrDefault(m=>m.Primary) }; captureFirst = firstMonitor;
        var firstField=StudioUi.Field(body,"Pantalla",firstMonitor);
        var secondMonitor=new ComboBox { ItemsSource=monitors,DisplayMemberPath="Name",SelectedItem=monitors.FirstOrDefault(m=>m.Device==options.SecondMonitorDevice) ?? monitors.FirstOrDefault(m=>m.Device!=(firstMonitor.SelectedItem as CaptureMonitor)?.Device) }; captureSecond = secondMonitor;
        var secondField=StudioUi.Field(body,"Segunda pantalla",secondMonitor);
        var layout=new ComboBox { ItemsSource=new[]{"Izquierda / derecha","Arriba / abajo","Pistas separadas · alternar"},SelectedIndex=(int)options.ScreenLayout }; captureLayout = layout;
        var layoutField=StudioUi.Field(body,"Distribución inicial",layout);
        var reverse=Check(body,"Invertir el orden de las pantallas",options.ReverseScreens); captureReverse = reverse;
        firstMonitor.SelectionChanged+=(_,_)=> { if((firstMonitor.SelectedItem as CaptureMonitor)?.Device==(secondMonitor.SelectedItem as CaptureMonitor)?.Device) secondMonitor.SelectedItem=monitors.FirstOrDefault(m=>m.Device!=(firstMonitor.SelectedItem as CaptureMonitor)?.Device); };
        var windows = CaptureTargets.Windows().Where(w => w.Handle != new WindowInteropHelper(this).Handle.ToInt64()).ToList();
        var window = new ComboBox { ItemsSource = windows, DisplayMemberPath = "Title", SelectedItem = windows.FirstOrDefault(w => w.Handle == options.WindowHandle), ToolTip = "Ventana que se grabará" }; var windowField = StudioUi.Field(body,"Ventana",window); captureWindow = window;
        var regionFields = new StackPanel(); body.Children.Add(regionFields);
        var region = StudioUi.Label($"{options.Width} × {options.Height} · {options.X}, {options.Y}","Muted",11); regionFields.Children.Add(region); captureRegion = region;
        var x = Input(regionFields,"Región X · px",options.X); var y = Input(regionFields,"Región Y · px",options.Y); var width = Input(regionFields,"Anchura · px",options.Width); var height = Input(regionFields,"Altura · px",options.Height);
        captureX = x; captureY = y; captureWidth = width; captureHeight = height;
        void ShowSourceFields()
        {
            windowField.Visibility = modes.SelectedIndex is 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
            regionFields.Visibility = modes.SelectedIndex is 1 or 3 ? Visibility.Visible : Visibility.Collapsed;
            firstField.Visibility=modes.SelectedIndex is 0 or 4?Visibility.Visible:Visibility.Collapsed;
            secondField.Visibility=layoutField.Visibility=reverse.Visibility=modes.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;
        }
        modes.SelectionChanged += (_,_) => ShowSourceFields(); ShowSourceFields();
        lastAudioError = null;
        if (!devicesLoaded && microphones.Count == 0 && outputs.Count == 0)
        {
            try { microphones = CaptureSources.AudioDevices(true); outputs = CaptureSources.AudioDevices(false); }
            catch (Exception e) { lastAudioError = e.Message; }
        }
        if (engine != null && !devicesLoaded && !cameraLoading)
        {
            cameraLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var sources = new CaptureSources(engine);
                    var found = await sources.CamerasAsync();
                    await Dispatcher.InvokeAsync(() => { cameras = found; devicesLoaded = true; cameraLoading = false; RefreshCameraSelector(); UpdateCommands(); });
                }
                catch (Exception e) { await Dispatcher.InvokeAsync(() => { cameraLoading = false; UpdateDeviceStatus(); ShowNotice("No se pudo detectar la cámara: " + e.Message); }); }
            });
        }
        var microphone = Check(body,"Grabar micrófono",options.Microphone); captureMicrophone = microphone;
        var mic = new ComboBox { ItemsSource = SavedDevices(microphones,options.MicrophoneId,"Dispositivo predeterminado"), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = options.MicrophoneId ?? "" }; var micField = StudioUi.Field(body,"Micrófono",mic); micCombo = mic; captureMic = mic;
        var system = Check(body,"Grabar audio del sistema",options.SystemAudio); captureSystem = system;
        var output = new ComboBox { ItemsSource = SavedDevices(outputs,options.OutputDeviceId,"Salida predeterminada"), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = options.OutputDeviceId ?? "" }; var outputField = StudioUi.Field(body,"Salida de audio",output); outputCombo = output; captureOutput = output;
        var micMeterRow = MeterRow(body,"Micrófono",out var micFill); micLevelFill = micFill;
        var sysMeterRow = MeterRow(body,"Sistema",out var sysFill); sysLevelFill = sysFill;
        EnsureAudioNotifications();
        void ShowAudioFields()
        {
            var micOn = microphone.IsChecked == true; var sysOn = system.IsChecked == true;
            micField.Visibility = micOn ? Visibility.Visible : Visibility.Collapsed;
            outputField.Visibility = sysOn ? Visibility.Visible : Visibility.Collapsed;
            micMeterRow.Visibility = micOn ? Visibility.Visible : Visibility.Collapsed;
            sysMeterRow.Visibility = sysOn ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowAudioFields();
        var cameraNames = cameras.Concat(options.CameraDevice == null ? [] : new[] { options.CameraDevice }).Distinct().ToArray();
        var camera = new ComboBox { ItemsSource = new[] { "Sin cámara" }.Concat(cameraNames), SelectedItem = options.CameraDevice ?? "Sin cámara" }; StudioUi.Field(body,"Cámara",camera); cameraCombo = camera; captureCamera = camera;
        microphone.Checked += (_,_) => { ShowAudioFields(); SaveCaptureOptions(); UpdateAudioMeters(); }; microphone.Unchecked += (_,_) => { ShowAudioFields(); SaveCaptureOptions(); UpdateAudioMeters(); };
        system.Checked += (_,_) => { ShowAudioFields(); SaveCaptureOptions(); UpdateAudioMeters(); }; system.Unchecked += (_,_) => { ShowAudioFields(); SaveCaptureOptions(); UpdateAudioMeters(); };
        deviceStatus = StudioUi.Label("","Muted",11); body.Children.Add(deviceStatus); UpdateDeviceStatus();
        firstMonitor.SelectionChanged += (_,_) => SaveCaptureOptions(); secondMonitor.SelectionChanged += (_,_) => SaveCaptureOptions();
        modes.SelectionChanged += (_,_) => SaveCaptureOptions();
        window.SelectionChanged += (_,_) => SaveCaptureOptions(); layout.SelectionChanged += (_,_) => SaveCaptureOptions();
        reverse.Checked += (_,_) => SaveCaptureOptions(); reverse.Unchecked += (_,_) => SaveCaptureOptions();
        mic.SelectionChanged += (_,_) => { SaveCaptureOptions(); UpdateAudioMeters(); }; output.SelectionChanged += (_,_) => { SaveCaptureOptions(); UpdateAudioMeters(); }; camera.SelectionChanged += (_,_) => SaveCaptureOptions();
        foreach (var field in new[] { x, y, width, height }) AutoCommit(field,() => { SaveCaptureOptions(); return Task.CompletedTask; });
        var select = StudioUi.ActionButton("select","Seleccionar región","Seleccionar región con el mouse",() =>
        {
            try
            {
                var useWindow = modes.SelectedIndex == 3;
                var target = useWindow ? CaptureTargets.Bounds(new CaptureOptions { Mode = CaptureMode.Window, WindowHandle = (window.SelectedItem as CaptureWindow)?.Handle ?? 0 }) : new CaptureBounds(CaptureTargets.GetSystemMetrics(76),CaptureTargets.GetSystemMetrics(77),CaptureTargets.GetSystemMetrics(78),CaptureTargets.GetSystemMetrics(79));
                var previous = WindowState; WindowState = WindowState.Minimized;
                try
                {
                    var picker = new RegionPicker(target); if (picker.ShowDialog() == true && picker.Selection is { } r) { x.Text = (r.X-(useWindow ? target.X : 0)).ToString(); y.Text = (r.Y-(useWindow ? target.Y : 0)).ToString(); width.Text = r.Width.ToString(); height.Text = r.Height.ToString(); modes.SelectedIndex = useWindow ? 3 : 1; SaveCaptureOptions(); }
                }
                finally { WindowState = previous; Activate(); }
            }
            catch (Exception e) { ShowNotice(e.Message); }
        }); regionFields.Children.Add(select);
        BuildScreenMontage(body);
        UpdateAudioMeters();
    }
    private async Task ImportCaptureAsync(string screen,List<CapturedMedia> extras)
    {
        var screens=recorder!.Screens.Count>0?recorder.Screens:[new CapturedScreen(screen,0,0,"Pantalla")];
        var screenAssets=new List<(CapturedScreen Screen,MediaAsset Asset)>();
        foreach(var item in screens) screenAssets.Add((item,await compositor!.InspectAsync(item.Path)));
        var totalDuration=screenAssets.Max(s=>s.Screen.Start+s.Asset.Duration);
        Editing.Change(p =>
        {
            var group=screenAssets.Count>1?Guid.NewGuid():(Guid?)null;
            foreach(var (item,asset) in screenAssets)
            {
                var visual=new StudioTrack { Name=item.Name }; p.Tracks.Insert(0,visual);
                var clip=new TimelineClip { Name=item.Name,SourcePath=item.Path,Kind=ClipKind.Video,Start=recordingInsertTime+item.Start,Duration=asset.Duration,SourceDuration=asset.Duration,CaptureGroupId=group,ScreenIndex=item.Index,ScreenName=item.Name };
                if(asset.Cursor!=null) { clip.Effects.Follow=Preferences.Capture.AutoZoom?FollowMode.Clicks:FollowMode.Off; clip.Effects.ShowCursor=asset.Cursor.ShowCursor; }
                visual.Clips.Add(clip); p.Library.Add(asset);
                if(item.Index==0) { Timeline.SelectedClipId=clip.Id; Timeline.SelectedTrackId=visual.Id; }
            }
            if(group is Guid id)
            {
                ScreenLayoutEditor.Apply(p,id,Preferences.Capture.ScreenLayout,Preferences.Capture.ReverseScreens);
                var visible=p.Clips.First(c=>c.CaptureGroupId==id && c.Opacity>0); Timeline.SelectedClipId=visible.Id; Timeline.SelectedTrackId=p.Tracks.First(t=>t.Clips.Contains(visible)).Id;
            }
        });
        foreach (var extra in extras)
        {
            try
            {
                var asset = await compositor!.InspectAsync(extra.Path); var duration = Math.Min(asset.Duration,totalDuration-extra.Start); if (duration < .04) continue;
                Editing.Change(p =>
                {
                    var track = new StudioTrack { Name=extra.Name,Kind=extra.Camera ? TrackKind.Visual : TrackKind.Audio }; if (extra.Camera) p.Tracks.Insert(0,track); else p.Tracks.Add(track);
                    var clip = new TimelineClip { Name=extra.Name,Kind=extra.Camera ? ClipKind.Video : ClipKind.Audio,SourcePath=extra.Path,Start=recordingInsertTime+extra.Start,Duration=duration,SourceDuration=asset.Duration };
                    if (extra.Camera) { clip.Scale=.25; clip.X=.82; clip.Y=.8; clip.Effects.CornerRadius=24; clip.Effects.Shadow=.45; }
                    track.Clips.Add(clip); p.Library.Add(asset);
                });
            }
            catch (Exception e) { ShowNotice("Se conservó la captura, pero no se pudo importar " + extra.Name + ": " + e.Message); }
        }
        dirty = true; Seek(recordingInsertTime+screens.Max(s=>s.Start)); await RefreshAsync();
    }
}

