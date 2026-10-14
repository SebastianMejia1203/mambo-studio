using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio;

public partial class MainWindow
{
    private TimelineClip? SelectedClip => Timeline.SelectedClipId is Guid id ? Editing.Clip(id) : null;
    private StudioTrack? SelectedTrack => Timeline.SelectedTrackId is Guid id ? Editing.Track(id) : null;
    private Button CommandButton(string command, string icon, string name)
    {
        var button = StudioUi.Button(icon, name, async () => await ExecuteAsync(command));
        if (!buttons.ContainsKey(command)) buttons[command] = [];
        buttons[command].Add(button); return button;
    }
    private void BuildMenus()
    {
        MenuBar.Items.Clear(); menuCommands.Clear();
        MenuItem Menu(string title) { var m = new MenuItem { Header = title }; MenuBar.Items.Add(m); return m; }
        void Item(MenuItem parent, string title, string command, string shortcut = "")
        {
            var item = new MenuItem { Header = title, InputGestureText = shortcut };
            item.Click += async (_, e) => { e.Handled = true; await ExecuteAsync(command); };
            parent.Items.Add(item); if (!menuCommands.ContainsKey(command)) menuCommands[command] = []; menuCommands[command].Add(item);
        }
        var file = Menu("Archivo");
        Item(file,"Nuevo proyecto","new","Ctrl+N"); Item(file,"Abrir proyecto…","open","Ctrl+O");
        Item(file,"Guardar","save","Ctrl+S"); Item(file,"Guardar como…","saveas","Ctrl+Shift+S"); file.Items.Add(new Separator());
        Item(file,"Importar archivos…","import","Ctrl+I"); Item(file,"Exportar…","export","Ctrl+E");
        var edit = Menu("Editar");
        foreach (var (name,cmd,key) in new[] { ("Deshacer","undo","Ctrl+Z"),("Rehacer","redo","Ctrl+Y"),("Cortar","cut","Ctrl+X"),("Copiar","copy","Ctrl+C"),("Pegar","paste","Ctrl+V"),("Eliminar clip","delete","Supr"),("Dividir en el cabezal","splitat","S"),("Marcar inicio","markin","I"),("Marcar final","markout","O"),("Restablecer recorte","reset","") }) Item(edit,name,cmd,key);
        var tracks = Menu("Pistas"); Item(tracks,"Añadir pista visual","addvisual"); Item(tracks,"Añadir pista de audio","addaudio"); Item(tracks,"Eliminar pista vacía","deletetrack"); Item(tracks,"Activar / desactivar pista","trackenabled"); Item(tracks,"Insertar texto…","text");
        var view = Menu("Ver"); Item(view,"Panel izquierdo","leftvisible"); Item(view,"Panel derecho","rightvisible");
        foreach (var name in new[] { "Archivos", "Inspector", "Movimiento", "Apariencia", "Transiciones", "Pantallas", "Lienzo", "Captura" })
        {
            var panel = new MenuItem { Header = name }; view.Items.Add(panel);
            Item(panel,"Mostrar a la izquierda","panelleft:" + name); Item(panel,"Mostrar a la derecha","panelright:" + name);
        }
        view.Items.Add(new Separator()); Item(view,"Ajustar al espacio","fit"); Item(view,"Modo oscuro","dark"); Item(view,"Modo claro","light");
        var config = Menu("Configuración"); Item(config,"Preferencias de vídeo y audio…","settings"); Item(config,"Fuentes y región de captura","panelright:Captura");
        UpdateCommands();
    }
    private void BuildToolbars()
    {
        void Tool(string cmd,string icon,string tip) => ToolBar.Items.Add(CommandButton(cmd,icon,tip));
        Tool("select","select","Seleccionar y mover · V"); Tool("trim","trim","Recortar · T"); Tool("split","split","Dividir · C"); ToolBar.Items.Add(StudioUi.Divider());
        Tool("undo","undo","Deshacer · Ctrl+Z"); Tool("redo","redo","Rehacer · Ctrl+Y"); ToolBar.Items.Add(StudioUi.Divider());
        Tool("cut","cut","Cortar · Ctrl+X"); Tool("copy","copy","Copiar · Ctrl+C"); Tool("paste","paste","Pegar · Ctrl+V"); Tool("delete","delete","Eliminar clip · Supr");
        Tool("markin","markin","Marcar inicio en el cabezal · I"); Tool("markout","markout","Marcar final en el cabezal · O"); Tool("reset","reset","Restablecer recorte"); ToolBar.Items.Add(StudioUi.Divider());
        Tool("text","text","Insertar texto"); Tool("addvisual","video","Añadir pista visual"); Tool("addaudio","audio","Añadir pista de audio"); Tool("deletetrack","delete","Eliminar pista vacía"); ToolBar.Items.Add(StudioUi.Divider());
        Tool("snap","snap","Ajustar a clips y cabezal · Mayús para omitir"); Tool("timelineout","zoomout","Alejar línea de tiempo"); Tool("timelinein","zoomin","Acercar línea de tiempo"); ToolBar.Items.Add(StudioUi.Divider());
        Tool("fit","fit","Ajustar al espacio"); Tool("previewout","zoomout","Alejar vista previa"); Tool("previewin","zoomin","Acercar vista previa");
        foreach (var (cmd,icon,tip) in new[] { ("start","start","Ir al principio · Inicio"),("back","back","Retroceder 5 segundos"),("prevframe","left","Fotograma anterior · ←"),("play","play","Reproducir / pausar · Espacio"),("stop","stop","Detener"),("nextframe","right","Fotograma siguiente · →"),("forward","forward","Adelantar 5 segundos"),("end","end","Ir al final · Fin"),("record","record","Grabar pantalla / detener · Ctrl+Shift+F9 para detener") })
        {
            var b = CommandButton(cmd,icon,tip); b.Width = 28; MediaBar.Children.Add(b);
            if (cmd == "play") playButton = b;
            if (cmd == "record") { recordButton = b; b.SetResourceReference(ForegroundProperty,"Danger"); }
        }
        ExportActions.Children.Add(StudioUi.Button("close","Cancelar exportación",() => exporting?.Cancel()));
    }
    private bool Enabled(string command)
    {
        if (command == "record") return recorder != null && (!busy || recording || recordStart != null);
        if (busy) return false;
        if(Timeline.SelectedEffect != null && command is "cut" or "copy" or "markin" or "markout" or "reset" or "splitat") return false;
        return command switch
        {
            "undo" => Editing.CanUndo, "redo" => Editing.CanRedo,
            "cut" or "copy" or "delete" or "markin" or "markout" or "reset" or "splitat" => SelectedClip != null,
            "paste" => clipboard != null,
            "deletetrack" => SelectedTrack?.Clips.Count == 0, "trackenabled" => SelectedTrack != null,
            "export" or "play" or "stop" or "start" or "end" or "back" or "forward" or "prevframe" or "nextframe" => Editing.Project.Duration > 0,
            _ => true
        };
    }
    private void UpdateCommands()
    {
        if (Timeline == null) return;
        RefreshPreviewSelection();
        Timeline.IsEnabled = !busy;
        foreach (var (command, list) in buttons) foreach (var button in list)
        {
            button.IsEnabled = Enabled(command);
            bool selected = command == "select" && Timeline.Tool == EditTool.Select || command == "trim" && Timeline.Tool == EditTool.Trim || command == "split" && Timeline.Tool == EditTool.Split || command == "snap" && Timeline.Snap;
            button.SetResourceReference(BackgroundProperty, selected ? "AccentSoft" : "Surface");
            if (command != "record") button.SetResourceReference(ForegroundProperty,selected ? "Accent" : "Text");
        }
        foreach (var (command,list) in menuCommands) foreach (var item in list)
        {
            item.IsEnabled = Enabled(command);
            item.IsChecked = command switch { "leftvisible" => Preferences.LeftVisible, "rightvisible" => Preferences.RightVisible, "dark" => Preferences.Dark, "light" => !Preferences.Dark, _ => false };
        }
        UpdateMediaButtons();
    }
    private void UpdateMediaButtons()
    {
        if (playButton != null) { playButton.Content = StudioUi.Icon(playing ? "pause" : "play"); playButton.ToolTip = playing ? "Pausar · Espacio" : "Reproducir · Espacio"; }
        if (recordButton != null) { recordButton.Content = StudioUi.Icon(recording || recordStart != null ? "stop" : "record"); recordButton.ToolTip = recording ? "Detener grabación" : recordStart != null ? "Cancelar cuenta atrás" : "Grabar pantalla"; }
    }
    public async Task ExecuteAsync(string command)
    {
        if (!Enabled(command)) return;
        try
        {
            var clip = SelectedClip;
            switch (command)
            {
                case "new": if (!ConfirmDiscard()) return; Pause(); Editing = new EditingSession(new StudioProject { Output = CloneOutput(Preferences.Defaults) }); projectPath = null; dirty = false; position = 0; await RefreshAsync(); break;
                case "open": var open = new OpenFileDialog { Filter = "Proyecto Studio|*.studio;*.screenstudio" }; if (open.ShowDialog(this) == true) await OpenFileAsync(open.FileName); break;
                case "import": var import = new OpenFileDialog { Title = "Importar vídeo, audio o imágenes", Multiselect = true, Filter = "Archivos multimedia|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.webp;*.tiff|Todos los archivos|*.*" }; if (import.ShowDialog(this) == true) await ImportFilesAsync(import.FileNames); break;
                case "save": await SaveAsync(false); break;
                case "saveas": await SaveAsync(true); break;
                case "export": await ExportAsync(); break;
                case "record": await RecordAsync(); break;
                case "select": Timeline.Tool = EditTool.Select; break;
                case "trim": Timeline.Tool = EditTool.Trim; break;
                case "split": Timeline.Tool = EditTool.Split; break;
                case "undo": Pause(); Editing.Undo(); dirty = true; await RefreshAsync(); break;
                case "redo": Pause(); Editing.Redo(); dirty = true; await RefreshAsync(); break;
                case "copy": clipboard = clip!.Clone(); break;
                case "cut": clipboard = clip!.Clone(); await ChangeAsync(_ => Editing.Owner(clip.Id)!.Clips.Remove(clip)); break;
                case "delete":
                    if(Timeline.SelectedEffect is {} effect && effect.ClipId==clip?.Id) EditTimelineEffect(new(effect,"delete"));
                    else await ChangeAsync(_ => Editing.Owner(clip!.Id)!.Clips.Remove(clip)); break;
                case "paste":
                    var copy = clipboard!.Clone(true); var target = SelectedTrack ?? Editing.Project.Tracks.FirstOrDefault(t => t.Kind == copy.Category);
                    if (target == null || target.Kind != copy.Category) throw new InvalidOperationException("Selecciona una pista compatible para pegar el clip.");
                    copy.Start = EditingSession.NextFree(target,position,copy.Duration); await ChangeAsync(_ => target.Clips.Add(copy)); Timeline.SelectedClipId = copy.Id; RefreshSidebars(); break;
                case "splitat": Pause(); Editing.Split(clip!.Id,position); dirty = true; await RefreshAsync(); break;
                case "markin":
                    if (position < clip!.Start || position >= clip.End - .04) throw new InvalidOperationException("Sitúa el cabezal dentro del clip.");
                    await ChangeAsync(_ => { var delta = position - clip.Start; clip.Start = position; clip.SourceOffset += delta; clip.Duration -= delta; }); break;
                case "markout":
                    if (position <= clip!.Start + .04 || position > clip.End) throw new InvalidOperationException("Sitúa el cabezal dentro del clip.");
                    await ChangeAsync(_ => clip.Duration = position - clip.Start); break;
                case "reset": await ChangeAsync(_ => { clip!.SourceOffset = 0; clip.Duration = clip.Kind is ClipKind.Video or ClipKind.Audio ? clip.SourceDuration : 5; }); break;
                case "addvisual": case "addaudio": Pause(); Editing.AddTrack(command == "addaudio" ? TrackKind.Audio : TrackKind.Visual); dirty = true; await RefreshAsync(); break;
                case "deletetrack": Pause(); Editing.RemoveTrack(SelectedTrack!.Id); dirty = true; await RefreshAsync(); break;
                case "trackenabled": await ChangeAsync(_ => SelectedTrack!.Enabled = !SelectedTrack.Enabled); break;
                case "text":
                    var text = StudioDialogs.Text(this); if (text == null) return;
                    await ChangeAsync(p => { var t = SelectedTrack?.Kind == TrackKind.Visual ? SelectedTrack : p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Visual); if (t == null) { t = new StudioTrack(); p.Tracks.Insert(0,t); } var c = new TimelineClip { Kind = ClipKind.Text, Text = text, Name = text.Split('\n')[0], Start = EditingSession.NextFree(t,position,5) }; t.Clips.Add(c); Timeline.SelectedClipId = c.Id; Timeline.SelectedTrackId = t.Id; }); break;
                case "play": if (playing) Pause(); else { if (position >= Editing.Project.Duration - .05) Seek(0); playing = true; playbackOrigin = position; playbackClock.Restart(); preview?.Update(position,true,true); } break;
                case "stop": Pause(); Seek(0); break;
                case "start": Seek(0); break;
                case "end": Seek(Editing.Project.Duration - 1d / Editing.Project.Output.FrameRate); break;
                case "back": Seek(position - 5); break;
                case "forward": Seek(position + 5); break;
                case "prevframe": Pause(); Seek(position - 1d / Editing.Project.Output.FrameRate); break;
                case "nextframe": Pause(); Seek(position + 1d / Editing.Project.Output.FrameRate); break;
                case "snap": Timeline.Snap = !Timeline.Snap; break;
                case "crossfade": await ChangeAsync(p => AudioCrossfade.Apply(p,Timeline.SelectedClipId ?? throw new InvalidOperationException("Selecciona un clip de audio."))); break;
                case "rangein": await ChangeAsync(p => { p.ExportIn = Math.Round(position,3); if (p.ExportOut > 0 && p.ExportOut <= p.ExportIn) p.ExportOut = 0; }); break;
                case "rangeout": { var end = Math.Round(position,3); if (end <= Editing.Project.ExportIn) throw new InvalidOperationException("El final del rango debe estar después del inicio (Alt+I)."); await ChangeAsync(p => p.ExportOut = end); break; }
                case "rangeclear": await ChangeAsync(p => { p.ExportIn = 0; p.ExportOut = 0; }); break;
                case "timelinein": Timeline.PixelsPerSecond = Math.Min(600,Timeline.PixelsPerSecond * 1.4); break;
                case "timelineout": Timeline.PixelsPerSecond = Math.Max(4,Timeline.PixelsPerSecond / 1.4); break;
                case "previewin": previewZoom = Math.Min(4,previewZoom * 1.25); ResizePreview(); break;
                case "previewout": previewZoom = Math.Max(.25,previewZoom / 1.25); ResizePreview(); break;
                case "fit": previewZoom = 1; ResizePreview(); Timeline.PixelsPerSecond = Math.Clamp((Timeline.ViewportWidth - 175) / Math.Max(10,Editing.Project.Duration + 1),4,300); break;
                case "leftvisible": Preferences.LeftVisible = !Preferences.LeftVisible; BuildSidebars(); SavePreferences(); break;
                case "rightvisible": Preferences.RightVisible = !Preferences.RightVisible; BuildSidebars(); SavePreferences(); break;
                case "dark": case "light": Preferences.Dark = command == "dark"; StudioUi.ApplyTheme(Preferences.Dark); NativeCaption.Apply(this,Preferences.Dark); SavePreferences(); break;
                case "settings": var settings = StudioDialogs.Settings(this,Editing.Project.Output); if (settings != null) { await ChangeAsync(p => p.Output = settings); Preferences.Defaults = CloneOutput(settings); SavePreferences(); } break;
                default:
                    if (command.StartsWith("panelleft:")) AddPanel(true,command[10..]);
                    else if (command.StartsWith("panelright:")) AddPanel(false,command[11..]);
                    break;
            }
        }
        catch (Exception e) { ShowNotice(e.Message); await RefreshAsync(); }
        Timeline.Refresh(); UpdateCommands();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (alt && !ctrl)
        {
            var range = e.Key switch { Key.I => "rangein", Key.O => "rangeout", Key.X => "rangeclear", _ => "" };
            if (range.Length > 0) { e.Handled = true; _ = ExecuteAsync(range); return; }
        }
        var command = ctrl ? e.Key switch { Key.N => "new",Key.O => "open",Key.S => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "saveas" : "save",Key.I => "import",Key.E => "export",Key.Z => "undo",Key.Y => "redo",Key.X => "cut",Key.C => "copy",Key.V => "paste",_ => "" } : e.Key switch { Key.Space => "play",Key.Delete => "delete",Key.V => "select",Key.T => "trim",Key.C => "split",Key.S => "splitat",Key.I => "markin",Key.O => "markout",Key.Left => "prevframe",Key.Right => "nextframe",Key.Home => "start",Key.End => "end",_ => "" };
        if (command.Length == 0) return;
        e.Handled = true; _ = ExecuteAsync(command);
    }
}


