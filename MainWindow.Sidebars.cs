using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenStudio.Models;
using ScreenStudio.UI;
namespace ScreenStudio;

public partial class MainWindow
{
    private bool includeCursor = true;
    private void SavePreferences() { try { Preferences.Save(preferencesPath); } catch (Exception e) { ShowNotice(e.Message); } }
    private static string PanelIcon(string name) => name switch { "Archivos" => "files", "Inspector" => "inspector", "Movimiento" => "select", "Captura" => "record", "Pantallas"=>"video", "Transiciones"=>"move", "Cola" => "export", "Lienzo" => "grid", _ => "canvas" };
    private void AddPanel(bool left, string name)
    {
        var list = left ? Preferences.LeftTabs : Preferences.RightTabs;
        if (!list.Contains(name)) list.Add(name);
        if (left) { Preferences.LeftVisible = true; Preferences.LeftCollapsed = false; }
        else { Preferences.RightVisible = true; Preferences.RightCollapsed = false; }
        BuildSidebars(); var tabs = sideTabs.FirstOrDefault(p => p.Left == left).Tabs;
        if (tabs != null) tabs.SelectedIndex = list.IndexOf(name);
        SavePreferences(); UpdateCommands();
    }
    private void BuildSidebars()
    {
        var selected = sideTabs.ToDictionary(s => s.Left,s => (s.Tabs.SelectedItem as TabItem)?.Tag as string);
        sideTabs.Clear();
        BuildSide(true,LeftHost,LeftColumn,selected.GetValueOrDefault(true));
        BuildSide(false,RightHost,RightColumn,selected.GetValueOrDefault(false));
    }
    private void BuildSide(bool left, Border host, ColumnDefinition column, string? selected)
    {
        var visible = left ? Preferences.LeftVisible : Preferences.RightVisible;
        var collapsed = left ? Preferences.LeftCollapsed : Preferences.RightCollapsed;
        var names = left ? Preferences.LeftTabs : Preferences.RightTabs;
        column.Width = new GridLength(!visible ? 0 : collapsed ? 44 : left ? 230 : 260);
        host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; host.Child = null;
        if (!visible) return;
        void Collapse(bool value)
        {
            if (left) Preferences.LeftCollapsed = value; else Preferences.RightCollapsed = value;
            BuildSidebars(); SavePreferences();
        }
        if (collapsed)
        {
            var rail = new StackPanel { Margin = new Thickness(4,8,4,0) };
            rail.Children.Add(StudioUi.Button(left ? "panelleft" : "panelright","Expandir panel",() => Collapse(false)));
            foreach (var name in names) rail.Children.Add(StudioUi.Button(PanelIcon(name),name,() => AddPanel(left,name)));
            host.Child = rail; return;
        }
        var dock = new DockPanel(); host.Child = dock;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var tabs = new TabControl { Tag=names.Count <= 1 ? "single" : "multiple" }; sideTabs.Add((left,tabs));
        var header = new DockPanel { Margin = new Thickness(12,4,6,4) };
        DockPanel.SetDock(header,Dock.Top); DockPanel.SetDock(actions,Dock.Right); header.Children.Add(actions);
        var caption = StudioUi.Label(selected ?? names.FirstOrDefault() ?? "Panel"); caption.FontWeight = FontWeights.SemiBold;
        header.Children.Add(caption); dock.Children.Add(header);
        tabs.SelectionChanged += (_,_) => { if (tabs.SelectedItem is TabItem tab) caption.Text = tab.Tag as string ?? "Panel"; };
        actions.Children.Add(StudioUi.Button(left ? "panelleft" : "panelright","Contraer panel",() => Collapse(true)));
        actions.Children.Add(StudioUi.Button("move","Mover pestaña al otro lado",() =>
        {
            if (tabs.SelectedItem is not TabItem item || item.Tag is not string name) return;
            names.Remove(name); AddPanel(!left,name);
        }));
        actions.Children.Add(StudioUi.Button("close","Ocultar panel",() => { if (left) Preferences.LeftVisible = false; else Preferences.RightVisible = false; BuildSidebars(); SavePreferences(); UpdateCommands(); }));
        foreach (var name in names.Where(n => n is "Archivos" or "Inspector" or "Lienzo" or "Movimiento" or "Apariencia" or "Captura" or "Pantallas" or "Transiciones" or "Cola"))
            tabs.Items.Add(new TabItem { Header = StudioUi.Icon(PanelIcon(name)), Tag = name, ToolTip = name, Content = PanelContent(name) });
        tabs.SelectedIndex = Math.Max(0,names.IndexOf(selected ?? "")); dock.Children.Add(tabs);
    }
    private void RefreshSidebars()
    {
        foreach (var (_,tabs) in sideTabs) foreach (TabItem item in tabs.Items) item.Content = PanelContent((string)item.Tag);
    }
    private UIElement PanelContent(string name)
    {
        if (name == "Archivos") return BuildLibrary();
        var body = new StackPanel { Margin = new Thickness(16,16,16,20) };
        if (name == "Movimiento") BuildMotion(body);
        else if (name == "Apariencia") BuildAppearance(body);
        else if (name == "Captura") BuildCapture(body);
        else if (name == "Cola") BuildQueue(body);
        else if (name == "Transiciones") BuildTransitions(body);
        else if (name == "Pantallas") { if(SelectedClip?.CaptureGroupId==null) body.Children.Add(StudioUi.Label("Selecciona una captura de dos pantallas","Muted",11)); else BuildScreenMontage(body); }
        else if (name == "Inspector") BuildInspector(body);
        else BuildCanvas(body);
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private static TextBox Input(StackPanel body,string title,object value)
    {
        var box = new TextBox { Text = Convert.ToString(value,CultureInfo.CurrentCulture) ?? "" }; StudioUi.Field(body,title,box); return box;
    }
    private static void AutoCommit(TextBox box,Func<Task> commit)
    {
        var modified = false; box.TextChanged += (_,_) => modified = true;
        box.LostKeyboardFocus += async (_,_) => { if (modified) { modified = false; await commit(); } };
        if (!box.AcceptsReturn) box.PreviewKeyDown += async (_,args) => { if (args.Key == Key.Enter) { modified = false; await commit(); args.Handled = true; } };
    }
    private void BuildInspector(StackPanel body)
    {
        if (SelectedClip is not { } clip)
        {
            if (SelectedTrack is not { } track) { body.Children.Add(StudioUi.Label("Selecciona un clip o una pista", "Muted",11)); return; }
            var name = Input(body,"Nombre de la pista",track.Name);
            AutoCommit(name,() => ApplyPanelAsync(() => track.Name = name.Text));
            var enabled = new CheckBox { Content = "Pista activa", IsChecked = track.Enabled, Margin = new Thickness(0,8,0,12) }; body.Children.Add(enabled);
            enabled.Checked += async (_,_) => await ApplyPanelAsync(() => track.Enabled = true);
            enabled.Unchecked += async (_,_) => await ApplyPanelAsync(() => track.Enabled = false);
            var delete = StudioUi.ActionButton("delete","Eliminar","Eliminar pista vacía",async () => await ExecuteAsync("deletetrack")); delete.IsEnabled = track.Clips.Count == 0; StudioUi.ActionRow(body).Children.Add(delete); return;
        }
        var clipName = Input(body,"Nombre",clip.Name);
        AutoCommit(clipName,() => ApplyPanelAsync(() => clip.Name = clipName.Text));
        if (clip.Kind != ClipKind.Audio)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,10) };
            if (clip.Kind == ClipKind.Video) tools.Children.Add(StudioUi.ActionButton("select","Cursor","Cursor y zoom",() => AddPanel(false,"Movimiento")));
            tools.Children.Add(StudioUi.ActionButton("canvas","Apariencia","Bordes, sombra y animaciones",() => AddPanel(false,"Apariencia"))); body.Children.Add(tools);
            var more=StudioUi.ActionRow(body); more.Children.Add(StudioUi.Button("move","Transiciones",()=>AddPanel(false,"Transiciones")));
            if(clip.CaptureGroupId!=null) more.Children.Add(StudioUi.Button("video","Montaje de pantallas",()=>AddPanel(false,"Pantallas")));
        }
        var start = Input(body,"Posición · segundos",Math.Round(clip.Start,3));
        AutoCommit(start,() => ApplyPanelAsync(() => clip.Start = StudioDialogs.Number(start)));
        var duration = Input(body,"Duración · segundos",Math.Round(clip.Duration,3));
        AutoCommit(duration,() => ApplyPanelAsync(() => { var value = StudioDialogs.Number(duration); if (value <= 0) throw new InvalidOperationException("La duración debe ser mayor que cero."); clip.Duration = value; }));
        TextBox? offset = null, scale = null, x = null, y = null, opacity = null, volume = null, speed = null, fadeIn = null, fadeOut = null, text = null, font = null, color = null;
        if (clip.Kind is ClipKind.Video or ClipKind.Audio) { offset = Input(body,"Desplazamiento en el archivo · segundos",Math.Round(clip.SourceOffset,3)); var o = offset; AutoCommit(o,() => ApplyPanelAsync(() => clip.SourceOffset = StudioDialogs.Number(o))); }
        if (clip.Kind is ClipKind.Video or ClipKind.Audio) { speed = Input(body,"Velocidad · %",Math.Round(clip.Speed * 100,2)); var sp = speed; AutoCommit(sp,() => ApplyPanelAsync(() => { var value = StudioDialogs.Number(sp) / 100; if (value < .25 || value > 4) throw new InvalidOperationException("La velocidad debe estar entre el 25 % y el 400 %."); clip.Speed = value; })); }
        if (clip.Kind == ClipKind.Audio)
        {
            volume = Input(body,"Volumen · %",clip.Volume * 100); var v = volume; AutoCommit(v,() => ApplyPanelAsync(() => clip.Volume = StudioDialogs.Number(v) / 100));
            fadeIn = Input(body,"Fundido de entrada · segundos",clip.FadeIn); var fi = fadeIn; AutoCommit(fi,() => ApplyPanelAsync(() => clip.FadeIn = StudioDialogs.Number(fi)));
            fadeOut = Input(body,"Fundido de salida · segundos",clip.FadeOut); var fo = fadeOut; AutoCommit(fo,() => ApplyPanelAsync(() => clip.FadeOut = StudioDialogs.Number(fo)));
            StudioUi.ActionRow(body).Children.Add(StudioUi.ActionButton("move","Fundido cruzado","Iguala fundidos con el clip de audio siguiente",async () => await ExecuteAsync("crossfade")));
        }
        else
        {
            scale = Input(body,"Escala · %",Math.Round(clip.Scale * 100,2)); x = Input(body,"Posición X · %",Math.Round(clip.X * 100,2)); y = Input(body,"Posición Y · %",Math.Round(clip.Y * 100,2)); opacity = Input(body,"Opacidad · %",clip.Opacity * 100);
            var s = scale; var px = x; var py = y; var op = opacity;
            AutoCommit(s,() => ApplyPanelAsync(() => clip.Scale = StudioDialogs.Number(s) / 100));
            AutoCommit(px,() => ApplyPanelAsync(() => clip.X = StudioDialogs.Number(px) / 100));
            AutoCommit(py,() => ApplyPanelAsync(() => clip.Y = StudioDialogs.Number(py) / 100));
            AutoCommit(op,() => ApplyPanelAsync(() => clip.Opacity = StudioDialogs.Number(op) / 100));
        }
        if (clip.Kind == ClipKind.Text)
        {
            text = Input(body,"Contenido",clip.Text); text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.MinHeight = 65;
            font = Input(body,"Tamaño de fuente",clip.FontSize); color = Input(body,"Color del texto",clip.TextColor);
            var t = text; var f = font; var c = color;
            AutoCommit(t,() => ApplyPanelAsync(() => clip.Text = t.Text));
            AutoCommit(f,() => ApplyPanelAsync(() => clip.FontSize = StudioDialogs.Number(f)));
            AutoCommit(c,() => ApplyPanelAsync(() => clip.TextColor = c.Text));
        }
        if (clip.Kind == ClipKind.Audio) Keyframes(body,"Animación · volumen","volumen",clip.VolumeKeys,clip.Volume);
        else
        {
            Keyframes(body,"Animación · opacidad","opacidad",clip.OpacityKeys,clip.Opacity);
            Keyframes(body,"Animación · escala · %","escala",clip.ScaleKeys,clip.Scale * 100,100);
            Keyframes(body,"Animación · posición X · %","posición X",clip.XKeys,clip.X * 100,100);
            Keyframes(body,"Animación · posición Y · %","posición Y",clip.YKeys,clip.Y * 100,100);
        }
    }
    private void Keyframes(StackPanel body,string title,string property,List<Keyframe> keys,double current,double divisor = 1)
    {
        var caption = StudioUi.Label(title); caption.FontWeight = FontWeights.SemiBold; caption.Margin = new Thickness(0,8,0,8); body.Children.Add(caption);
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index]; var at = index;
            var row = new Grid { Margin = new Thickness(0,0,0,8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var when = StudioUi.Label("t=" + Math.Round(key.Time,3).ToString(CultureInfo.CurrentCulture) + " s","Muted",11);
            when.Margin = new Thickness(0,0,8,0); when.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(when,0); row.Children.Add(when);
            var box = new TextBox { Text = Convert.ToString(Math.Round(key.Value * divisor,4),CultureInfo.CurrentCulture) ?? "" };
            Grid.SetColumn(box,1); row.Children.Add(box);
            AutoCommit(box,() => ApplyPanelAsync(() => { keys[at] = keys[at] with { Value = StudioDialogs.Number(box) / divisor }; }));
            var remove = StudioUi.Button("delete","Eliminar keyframe",async () => await ApplyPanelAsync(() => keys.RemoveAt(at)));
            Grid.SetColumn(remove,2); row.Children.Add(remove);
            body.Children.Add(row);
        }
        StudioUi.ActionRow(body).Children.Add(StudioUi.ActionButton("plus","Añadir keyframe aquí","Fija " + property + " en la posición del cabezal",async () => await ApplyPanelAsync(() =>
        {
            var time = Math.Max(0,Math.Round(position - SelectedClipStart(),3));
            keys.RemoveAll(k => Math.Abs(k.Time - time) < .0001);
            keys.Add(new Keyframe(time,current / divisor));
            keys.Sort((a,b) => a.Time.CompareTo(b.Time));
        })));
    }
    private double SelectedClipStart() => SelectedClip?.Start ?? position;
    private async Task ApplyPanelAsync(Action action)
    {
        if (busy) return;
        try { await ChangeAsync(_ => action()); } catch (Exception e) { ShowNotice(e.Message); await RefreshAsync(); }
    }
    private void BuildCanvas(StackPanel body)
    {
        var project = Editing.Project;
        var presets = new[] { ("Horizontal 16:9 · 1920 × 1080",1920,1080), ("Vertical 9:16 · 1080 × 1920",1080,1920), ("Cuadrado 1:1 · 1080 × 1080",1080,1080), ("Retrato 4:5 · 1080 × 1350",1080,1350), ("Personalizado…",0,0) };
        var presetIndex = Array.FindIndex(presets,p => p.Item2 == project.Width && p.Item3 == project.Height);
        var preset = new ComboBox { ItemsSource = presets.Select(p => p.Item1).ToArray(), SelectedIndex = presetIndex < 0 ? 4 : presetIndex };
        StudioUi.Field(body,"Formato",preset);
        var sizeLabel = StudioUi.Label("Tamaño · px", "Muted", 11); sizeLabel.Margin = new Thickness(0, 0, 0, 5); body.Children.Add(sizeLabel);
        var sizeRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var width = new TextBox { Text = Convert.ToString(project.Width, CultureInfo.CurrentCulture) ?? "", ToolTip = "Anchura · px" };
        Grid.SetColumn(width, 0); sizeRow.Children.Add(width);
        var cross = StudioUi.Label("×", "Muted", 13); cross.Margin = new Thickness(8, 0, 8, 0); cross.VerticalAlignment = VerticalAlignment.Center; cross.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(cross, 1); sizeRow.Children.Add(cross);
        var height = new TextBox { Text = Convert.ToString(project.Height, CultureInfo.CurrentCulture) ?? "", ToolTip = "Altura · px" };
        Grid.SetColumn(height, 2); sizeRow.Children.Add(height);
        body.Children.Add(sizeRow);
        var padding = Input(body,"Margen · px",project.Padding); var background = Input(body,"Fondo · hexadecimal",project.Background);
        var gradient = Check(body,"Fondo degradado",project.GradientBackground); var end = Input(body,"Segundo color",project.BackgroundEnd);
        AutoCommit(width,() => ApplyPanelAsync(() => project.Width = checked((int)StudioDialogs.Number(width))));
        AutoCommit(height,() => ApplyPanelAsync(() => project.Height = checked((int)StudioDialogs.Number(height))));
        AutoCommit(padding,() => ApplyPanelAsync(() => project.Padding = checked((int)StudioDialogs.Number(padding))));
        AutoCommit(background,() => ApplyPanelAsync(() => project.Background = background.Text));
        AutoCommit(end,() => ApplyPanelAsync(() => project.BackgroundEnd = end.Text));
        gradient.Checked += async (_,_) => await ApplyPanelAsync(() => project.GradientBackground = true);
        gradient.Unchecked += async (_,_) => await ApplyPanelAsync(() => project.GradientBackground = false);
        preset.SelectionChanged += async (_,_) =>
        {
            if (preset.SelectedIndex < 0 || preset.SelectedIndex >= 4) return;
            var size = presets[preset.SelectedIndex];
            await ApplyPanelAsync(() => { project.Width = size.Item2; project.Height = size.Item3; });
        };
    }
}



