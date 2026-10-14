using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenStudio.Models;
namespace ScreenStudio.UI;

public sealed class MediaLibraryState
{
    public string Search = "";
    public int Filter, Sort;
    public bool Tiles = true;
    public string? SelectedPath;
}

public sealed class MediaLibraryView : Grid
{
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    public IReadOnlyList<MediaAsset> VisibleAssets { get; private set; } = [];
    public MediaLibraryView(IEnumerable<MediaAsset> library, Dictionary<string,ImageSource> thumbnails, MediaLibraryState state,
        Action import, Action<MediaAsset> insert, Action<MediaAsset> remove, Func<MediaAsset,bool> canRemove)
    {
        Margin = new Thickness(12,12,12,10); var assets = library.ToArray();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tools = new DockPanel { LastChildFill = false, Margin = new Thickness(0,0,0,4) };
        var add = StudioUi.Button("plus","Insertar selección en la línea de tiempo",() => { if (list.SelectedItem is ListBoxItem { Tag: MediaAsset a }) insert(a); });
        var delete = StudioUi.Button("delete","Quitar de la biblioteca · sólo archivos sin usar",() => { if (list.SelectedItem is ListBoxItem { Tag: MediaAsset a }) remove(a); });
        var views = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(views,Dock.Right); tools.Children.Add(views);
        var tiles = StudioUi.Button("grid","Miniaturas",() => { }); var rows = StudioUi.Button("list","Lista detallada",() => { }); views.Children.Add(tiles); views.Children.Add(rows);
        var imports = StudioUi.ActionButton("import","Importar","Importar a la biblioteca",import); tools.Children.Add(imports); Children.Add(tools);
        var search = new TextBox { Text = state.Search, ToolTip = "Buscar archivos por nombre", Margin = new Thickness(0,8,0,8) };
        System.Windows.Automation.AutomationProperties.SetName(search,"Buscar archivos");
        var searchHost = new Grid(); searchHost.Children.Add(search); var hint = StudioUi.Label("Buscar archivos…","Muted"); hint.Margin = new Thickness(7,0,0,0); hint.IsHitTestVisible = false; searchHost.Children.Add(hint); SetRow(searchHost,1); Children.Add(searchHost);
        var filters = new Grid { Margin = new Thickness(0,0,0,8) }; filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition());
        var filter = new ComboBox { ItemsSource = new[] { "Todos", "Vídeo", "Imágenes", "Audio" }, SelectedIndex = state.Filter, ToolTip = "Tipo de archivo", Margin = new Thickness(0,0,4,0) };
        var sort = new ComboBox { ItemsSource = new[] { "Nombre", "Duración", "Tipo" }, SelectedIndex = state.Sort, ToolTip = "Ordenar por", Margin = new Thickness(4,0,0,0) }; SetColumn(sort,1); filters.Children.Add(filter); filters.Children.Add(sort); SetRow(filters,2); Children.Add(filters);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto); SetRow(list,3); Children.Add(list);
        var footer = new DockPanel { LastChildFill=false, Margin=new Thickness(0,8,0,0) }; SetRow(footer,4); Children.Add(footer);
        var selectionActions = new StackPanel { Orientation=Orientation.Horizontal }; DockPanel.SetDock(selectionActions,Dock.Right); selectionActions.Children.Add(add); selectionActions.Children.Add(delete); footer.Children.Add(selectionActions);
        var count = StudioUi.Label("","Muted",10); count.Margin = new Thickness(4,0,0,0); footer.Children.Add(count);
        void Selection()
        {
            var asset = (list.SelectedItem as ListBoxItem)?.Tag as MediaAsset; state.SelectedPath = asset?.Path;
            add.IsEnabled = asset != null; delete.IsEnabled = asset != null && canRemove(asset);
        }
        void Refresh()
        {
            tiles.SetResourceReference(BackgroundProperty,state.Tiles ? "AccentSoft" : "Surface"); rows.SetResourceReference(BackgroundProperty,state.Tiles ? "Surface" : "AccentSoft");
            var selectedPath = state.SelectedPath;
            VisibleAssets = Query(assets,state).ToArray(); list.Items.Clear(); state.SelectedPath = selectedPath;
            hint.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var asset in VisibleAssets)
            {
                var card = new Grid { Margin = new Thickness(0,3,0,5) };
                if (state.Tiles) { card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(105) }); card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                else { card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) }); card.ColumnDefinitions.Add(new ColumnDefinition()); }
                var thumb = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Height = state.Tiles ? 105 : 42, VerticalAlignment = VerticalAlignment.Top }; thumb.SetResourceReference(Border.BackgroundProperty,"Raised");
                if (thumbnails.TryGetValue(asset.Path,out var image)) thumb.Child = new Image { Source = image, Stretch = Stretch.Uniform };
                else thumb.Child = StudioUi.Icon(asset.Kind switch { ClipKind.Audio => "audio", ClipKind.Image => "image", _ => "video" },25);
                card.Children.Add(thumb);
                var detail = new StackPanel { Margin = new Thickness(state.Tiles ? 0 : 9, state.Tiles ? 7 : 0, 0, 0) };
                var title = StudioUi.Label(asset.Name); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; detail.Children.Add(title);
                var duration = asset.Kind == ClipKind.Image ? "Imagen" : TimeSpan.FromSeconds(asset.Duration).ToString(@"mm\:ss");
                var dimensions = asset.Width > 0 ? $" · {asset.Width} × {asset.Height}" : " · Audio";
                var metadata = StudioUi.Label(duration + dimensions,"Muted",10); metadata.Margin = new Thickness(0,4,0,0); detail.Children.Add(metadata);
                if (asset.Cursor != null) { var label = StudioUi.Label("Cursor y clics editables","Accent",10); label.Margin = new Thickness(0,4,0,0); detail.Children.Add(label); }
                if (state.Tiles) SetRow(detail,1); else SetColumn(detail,1); card.Children.Add(detail);
                var item = new ListBoxItem { Content = card, Tag = asset, ToolTip = asset.Path, Padding = new Thickness(5) }; list.Items.Add(item);
                if (asset.Path == state.SelectedPath) list.SelectedItem = item;
            }
            count.Text = VisibleAssets.Count == 0 ? "Sin archivos que mostrar" : $"{VisibleAssets.Count} de {assets.Length} archivos"; Selection();
        }
        tiles.Click += (_,_) => { state.Tiles = true; Refresh(); }; rows.Click += (_,_) => { state.Tiles = false; Refresh(); };
        search.TextChanged += (_,_) => { state.Search = search.Text; Refresh(); };
        filter.SelectionChanged += (_,_) => { state.Filter = filter.SelectedIndex; Refresh(); };
        sort.SelectionChanged += (_,_) => { state.Sort = sort.SelectedIndex; Refresh(); };
        list.SelectionChanged += (_,_) => Selection();
        list.MouseDoubleClick += (_,_) => { if (list.SelectedItem is ListBoxItem { Tag: MediaAsset a }) insert(a); };
        Point origin = default; list.PreviewMouseLeftButtonDown += (_,e) => origin = e.GetPosition(list);
        list.PreviewMouseMove += (_,e) => { if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(list)-origin).Length > 8 && list.SelectedItem is ListBoxItem { Tag: MediaAsset a }) DragDrop.DoDragDrop(list,new DataObject("StudioAsset",a.Path),DragDropEffects.Copy); };
        Refresh();
    }
    public static IEnumerable<MediaAsset> Query(IEnumerable<MediaAsset> library, MediaLibraryState state)
    {
        var query = library.Where(a => a.Name.Contains(state.Search,StringComparison.OrdinalIgnoreCase) && (state.Filter == 0 || a.Kind == (state.Filter == 1 ? ClipKind.Video : state.Filter == 2 ? ClipKind.Image : ClipKind.Audio)));
        return state.Sort switch { 1 => query.OrderByDescending(a => a.Duration), 2 => query.OrderBy(a => a.Kind).ThenBy(a => a.Name), _ => query.OrderBy(a => a.Name,StringComparer.CurrentCultureIgnoreCase) };
    }
}

