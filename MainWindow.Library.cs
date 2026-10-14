using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScreenStudio.Models;
using ScreenStudio.UI;
namespace ScreenStudio;
public partial class MainWindow
{
    private readonly MediaLibraryState libraryState = new();
    private UIElement BuildLibrary() => new MediaLibraryView(Editing.Project.Library,preview?.Thumbnails ?? [],libraryState,
        async () =>
        {
            if (busy) return;
            var dialog = new OpenFileDialog { Title = "Importar a la biblioteca", Multiselect = true, Filter = "Multimedia|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.webp;*.tiff|Todos|*.*" };
            if (dialog.ShowDialog(this) == true) await ImportFilesAsync(dialog.FileNames,libraryOnly:true);
        }, async asset =>
        {
            if (busy) return;
            try { Pause(); AddAsset(asset,null,position); dirty = true; await RefreshAsync(); } catch (Exception e) { ShowNotice(e.Message); }
        }, async asset =>
        {
            if (busy || Editing.Project.Clips.Any(c => c.SourcePath == asset.Path)) return;
            await ChangeAsync(p => p.Library.RemoveAll(a => a.Path == asset.Path));
        }, asset => !Editing.Project.Clips.Any(c => c.SourcePath == asset.Path));
    private static CheckBox Check(StackPanel body,string title,bool value)
    {
        var check = new CheckBox { Content = title, IsChecked = value }; body.Children.Add(check); return check;
    }
    private void BuildMotion(StackPanel body)
    {
        if (SelectedClip is not { Kind: ClipKind.Video } clip) { body.Children.Add(StudioUi.Label("Selecciona un clip de vídeo", "Muted",11)); return; }
        BuildSelectedEffect(body,clip);
        var trace = Editing.Project.Library.FirstOrDefault(a => a.Path == clip.SourcePath)?.Cursor;
        var e = clip.Effects;
        var modes = trace == null ? new[] { "Sin zoom", "Zoom manual" } : new[] { "Sin zoom", "Zoom en clics", "Seguir cursor", "Zoom manual" };
        var mode = new ComboBox { ItemsSource = modes, SelectedIndex = trace == null ? e.Follow == FollowMode.Manual ? 1 : 0 : (int)e.Follow };
        StudioUi.Field(body,"Modo",mode);
        if (trace == null) body.Children.Add(StudioUi.Label("Este archivo no tiene datos del mouse. Las nuevas capturas guardan posiciones y clics.","Muted",11));
        var zoom = Input(body,"Aumento · 1–4×",e.Zoom); var follow = Input(body,"Respuesta del seguimiento · segundos",e.FollowSeconds);
        var transition = Input(body,"Transición del zoom · segundos",e.TransitionSeconds); var hold = Input(body,"Mantener zoom tras clic · segundos",e.HoldSeconds);
        var x = Input(body,"Foco manual X · %",e.FocusX*100); var y = Input(body,"Foco manual Y · %",e.FocusY*100);
        void MotionFields()
        {
            var selected = trace == null ? mode.SelectedIndex == 1 ? FollowMode.Manual : FollowMode.Off : (FollowMode)mode.SelectedIndex;
            void Show(TextBox box,bool value) => ((FrameworkElement)box.Parent).Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Show(zoom,selected != FollowMode.Off); Show(follow,selected is FollowMode.Always or FollowMode.Clicks);
            Show(transition,selected == FollowMode.Clicks); Show(hold,selected == FollowMode.Clicks);
            Show(x,selected == FollowMode.Manual); Show(y,selected == FollowMode.Manual);
        }
        mode.SelectionChanged += (_,_) => MotionFields(); MotionFields();
        var cursorTitle=StudioUi.Label("Cursor"); cursorTitle.FontWeight=FontWeights.SemiBold; cursorTitle.Margin=new Thickness(0,8,0,8); body.Children.Add(cursorTitle);
        var show = Check(body,"Mostrar cursor",e.ShowCursor); var clicks = Check(body,"Resaltar clics",e.ShowClicks); var idle = Check(body,"Ocultar cursor inmóvil",e.HideIdleCursor);
        show.IsEnabled = clicks.IsEnabled = idle.IsEnabled = trace != null;
        var size = Input(body,"Tamaño del cursor · px a 1080p",e.CursorSize); var smooth = Input(body,"Suavizado del cursor · segundos",e.CursorSmoothSeconds); size.IsEnabled = smooth.IsEnabled = trace != null;
        var cursorImage = e.CursorImage;
        var imagePreview = new Image { Source=CursorArtwork.Get(e), Width=32, Height=32, HorizontalAlignment=HorizontalAlignment.Left, Margin=new Thickness(0,4,0,8), Visibility=cursorImage == null ? Visibility.Collapsed : Visibility.Visible }; body.Children.Add(imagePreview);
        var cursorActions = StudioUi.ActionRow(body); cursorActions.IsEnabled = trace != null;
        cursorActions.Children.Add(StudioUi.ActionButton("cursorimage","Imagen…","Cambiar imagen del cursor",() =>
        {
            var dialog = new OpenFileDialog { Title="Imagen del cursor",Filter="Imagen de cursor|*.png;*.ico;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) != true) return;
            try { cursorImage=CursorArtwork.Import(dialog.FileName); imagePreview.Source=CursorArtwork.Decode(cursorImage); imagePreview.Visibility=Visibility.Visible; UpdateLiveEffects(clip.Id,v=>v.CursorImage=cursorImage); }
            catch (Exception error) { ShowNotice(error.Message); }
        }));
        cursorActions.Children.Add(StudioUi.Button("reset","Restablecer flecha del cursor",() => { cursorImage=null; imagePreview.Source=null; imagePreview.Visibility=Visibility.Collapsed; UpdateLiveEffects(clip.Id,v=>v.CursorImage=null); }));
        var hotspotX = Input(body,"Punto activo X · %",e.CursorHotspotX*100); var hotspotY = Input(body,"Punto activo Y · %",e.CursorHotspotY*100);
        ((FrameworkElement)hotspotX.Parent).SetBinding(VisibilityProperty,new System.Windows.Data.Binding("Visibility") { Source=imagePreview });
        ((FrameworkElement)hotspotY.Parent).SetBinding(VisibilityProperty,new System.Windows.Data.Binding("Visibility") { Source=imagePreview });
        ((FrameworkElement)hotspotX.Parent).ToolTip = "Posición horizontal de la punta o punto de clic dentro de la imagen";
        ((FrameworkElement)hotspotY.Parent).ToolTip = "Posición vertical de la punta o punto de clic dentro de la imagen";
        mode.SelectionChanged += (_,_) => UpdateLiveEffects(clip.Id,v=>SetFollow(v,trace == null ? mode.SelectedIndex == 1 ? FollowMode.Manual : FollowMode.Off : (FollowMode)mode.SelectedIndex));
        void Toggle(CheckBox box,Action<ClipEffects,bool> set)
        {
            box.Checked += (_,_)=>UpdateLiveEffects(clip.Id,v=>set(v,true));
            box.Unchecked += (_,_)=>UpdateLiveEffects(clip.Id,v=>set(v,false));
        }
        void Number(TextBox box,Action<ClipEffects,double> set)
        {
            var modified=false; box.TextChanged+=(_,_)=>modified=true;
            void Commit()
            {
                try { var value=StudioDialogs.Number(box); UpdateLiveEffects(clip.Id,v=>set(v,value)); }
                catch(Exception error) { ShowNotice(error.Message); }
            }
            box.LostKeyboardFocus += (_,_)=> { if(modified) { modified=false; Commit(); } };
            box.PreviewKeyDown += (_,args)=> { if(args.Key==System.Windows.Input.Key.Enter) { modified=false; Commit(); args.Handled=true; } };
        }
        Toggle(show,(v,n)=>v.ShowCursor=n); Toggle(clicks,(v,n)=>v.ShowClicks=n); Toggle(idle,(v,n)=>v.HideIdleCursor=n);
        Number(zoom,(v,n)=>v.Zoom=n); Number(follow,(v,n)=>v.FollowSeconds=n);
        Number(transition,(v,n)=>v.TransitionSeconds=n); Number(hold,(v,n)=>v.HoldSeconds=n);
        Number(x,(v,n)=>v.FocusX=n/100); Number(y,(v,n)=>v.FocusY=n/100);
        Number(size,(v,n)=>v.CursorSize=n); Number(smooth,(v,n)=>v.CursorSmoothSeconds=n);
        Number(hotspotX,(v,n)=>v.CursorHotspotX=n/100); Number(hotspotY,(v,n)=>v.CursorHotspotY=n/100);
    }
    private void BuildAppearance(StackPanel body)
    {
        if (SelectedClip is not { } clip || clip.Kind == ClipKind.Audio) { body.Children.Add(StudioUi.Label("Selecciona un clip visual", "Muted",11)); return; }
        var e = clip.Effects;
        var radius = Input(body,"Esquinas redondeadas · px",e.CornerRadius); var border = Input(body,"Borde · px",e.BorderWidth); var color = Input(body,"Color del borde",e.BorderColor);
        var shadow = Input(body,"Intensidad de sombra · %",e.Shadow*100); var fade = Input(body,"Entrada y salida animada · segundos",e.FadeSeconds);
        LiveNumber(clip.Id,radius,(v,n)=>v.CornerRadius=n); LiveNumber(clip.Id,border,(v,n)=>v.BorderWidth=n); LiveText(clip.Id,color,(v,n)=>v.BorderColor=n);
        LiveNumber(clip.Id,shadow,(v,n)=>v.Shadow=n/100); LiveNumber(clip.Id,fade,(v,n)=>v.FadeSeconds=n);
        var blurTitle=StudioUi.Label("Desenfoque de región"); blurTitle.FontWeight=FontWeights.SemiBold; blurTitle.Margin=new Thickness(0,8,0,8); body.Children.Add(blurTitle);
        var blurX = Input(body,"Zona X · %",e.BlurX*100); var blurY = Input(body,"Zona Y · %",e.BlurY*100);
        var blurW = Input(body,"Zona ancha · %",e.BlurW*100); var blurH = Input(body,"Zona alta · %",e.BlurH*100);
        var blurAmount = Input(body,"Intensidad · 0–50",e.BlurAmount);
        LiveNumber(clip.Id,blurX,(v,n)=>v.BlurX=n/100); LiveNumber(clip.Id,blurY,(v,n)=>v.BlurY=n/100);
        LiveNumber(clip.Id,blurW,(v,n)=>v.BlurW=n/100); LiveNumber(clip.Id,blurH,(v,n)=>v.BlurH=n/100);
        LiveNumber(clip.Id,blurAmount,(v,n)=>v.BlurAmount=n);
        var chromaTitle=StudioUi.Label("Croma"); chromaTitle.FontWeight=FontWeights.SemiBold; chromaTitle.Margin=new Thickness(0,8,0,8); body.Children.Add(chromaTitle);
        var chromaColor = Input(body,"Color de croma",e.ChromaColor);
        var chromaSimilarity = Input(body,"Similitud · %",e.ChromaSimilarity*100); var chromaBlend = Input(body,"Suavizado de borde · %",e.ChromaBlend*100);
        LiveText(clip.Id,chromaColor,(v,n)=>v.ChromaColor=n);
        LiveNumber(clip.Id,chromaSimilarity,(v,n)=>v.ChromaSimilarity=n/100); LiveNumber(clip.Id,chromaBlend,(v,n)=>v.ChromaBlend=n/100);
        body.Children.Add(StudioUi.Label("El croma se aplica al exportar; la vista previa muestra el original.","Muted",11));
    }
}


