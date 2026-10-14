using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio;
using ScreenStudio.Controls;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
internal static partial class Program
{
    private static async Task PreviewTransformChecks()
    {
        var project=await StudioProject.LoadAsync(Path.Combine(directory,"sample.studio"));
        var photo=project.Clips.First(c=>c.Kind==ClipKind.Image); var video=project.Clips.First(c=>c.Kind==ClipKind.Video);
        var timing=project.Clips.ToDictionary(c=>c.Id,c=>(c.Start,c.Duration,c.SourceOffset));
        var original=await File.ReadAllBytesAsync(photo.SourcePath);
        var window=new MainWindow(new StudioPreferences(),Path.Combine(directory,"preview-transform-preferences.json"));
        window.Editing.Change(p=> { p.Name="Tamaño en la vista previa"; p.Width=project.Width; p.Height=project.Height; p.Library=project.Library; p.Tracks=project.Tracks; });
        await window.RefreshAsync(); window.Show(); window.Seek(.75); await Task.Delay(100); window.UpdateLayout();
        var overlay=(PreviewTransformControl)window.FindName("PreviewTransform"); var stage=(Canvas)window.FindName("Stage");
        var timeline=(TimelineControl)window.FindName("Timeline"); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var renderer=(PreviewRenderer)typeof(MainWindow).GetField("preview",flags)!.GetValue(window)!;
        object? Call(string method,params object[] args)=>typeof(PreviewTransformControl).GetMethod(method,flags)!.Invoke(overlay,args);
        Point Pointer(Point source)=>stage.TranslatePoint(source,overlay);
        Point Center(Rect bounds)=>new(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
        Rect Bounds(Guid id)=>renderer.VisualBounds(id)!.Value;
        void Begin(Point source)
        {
            Check((bool)Call("BeginGesture",Pointer(source))! && overlay.IsMouseCaptured,"preview gesture captures the pointer in the native editor");
        }
        try
        {
            var initial=Bounds(photo.Id);
            Begin(Center(initial)); Call("EndGesture");
            Check(timeline.SelectedClipId==photo.Id && overlay.SelectionBounds!=null,"clicking overlapping images selects the upper visible element");
            Begin(initial.BottomRight);
            Call("UpdateGesture",Pointer(new Point(initial.Left+initial.Width*1.5,initial.Top+initial.Height*1.5)));
            Check(Math.Abs(window.Editing.Clip(photo.Id)!.Scale-.25)<.001 && Math.Abs(Bounds(photo.Id).Width-initial.Width*1.5)<2,"resize preview updates live before committing the project");
            Call("EndGesture");
            var enlarged=window.Editing.Clip(photo.Id)!;
            Check(Math.Abs(enlarged.Scale-.375)<.001 && Math.Abs(Bounds(photo.Id).Left-initial.Left)<1 && Math.Abs(Bounds(photo.Id).Top-initial.Top)<1,"corner resize preserves proportions and the opposite anchor");
            await window.ExecuteAsync("undo");
            Check(Math.Abs(window.Editing.Clip(photo.Id)!.Scale-.25)<.001,"one undo restores the entire resize gesture");
            await window.ExecuteAsync("redo");
            Check(Math.Abs(window.Editing.Clip(photo.Id)!.Scale-.375)<.001,"redo restores preview size and position");
            timeline.SelectedClipId=video.Id; await window.ExecuteAsync("select"); window.UpdateLayout();
            var videoBounds=Bounds(video.Id); Begin(new Point(videoBounds.Right,videoBounds.Top+videoBounds.Height/2));
            Call("UpdateGesture",Pointer(new Point(videoBounds.Left+videoBounds.Width*.75,videoBounds.Top+videoBounds.Height/2))); Call("EndGesture");
            Check(Math.Abs(window.Editing.Clip(video.Id)!.Scale-.75)<.001 && Math.Abs(Bounds(video.Id).Width/videoBounds.Width-.75)<.01,"side handles also resize video layers");
            await window.ExecuteAsync("undo");
            var beforeMove=Bounds(photo.Id); Begin(Center(beforeMove)); Call("UpdateGesture",Pointer(Center(beforeMove)+new Vector(80,30)));
            overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(overlay)!,0,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent });
            Check(!overlay.IsTransforming && Bounds(photo.Id)==beforeMove && window.Editing.CanRedo,"Escape cancels a preview gesture without changing undo history");
            Begin(Center(beforeMove)); Call("UpdateGesture",Pointer(Center(beforeMove)+new Vector(80,30))); Call("EndGesture");
            Check(Math.Abs(Bounds(photo.Id).Left-beforeMove.Left-80)<1 && Math.Abs(Bounds(photo.Id).Top-beforeMove.Top-30)<1,"dragging inside an element moves it on the canvas");
            await window.ExecuteAsync("previewin"); window.UpdateLayout();
            var zoomed=Bounds(photo.Id); Begin(zoomed.BottomRight);
            Call("UpdateGesture",Pointer(new Point(zoomed.Left+zoomed.Width*.8,zoomed.Top+zoomed.Height*.8))); Call("EndGesture");
            Check(Math.Abs(window.Editing.Clip(photo.Id)!.Scale-.3)<.001,"preview magnification does not change resize coordinates");
            Render(window,1440,930,"studio-preview-resize.png");
            var saved=Path.Combine(directory,"preview-transform.studio"); await window.Editing.Project.SaveAsync(saved); var loaded=await StudioProject.LoadAsync(saved);
            var updated=window.Editing.Clip(photo.Id)!; var reopened=loaded.Clips.First(c=>c.Id==photo.Id);
            Check(reopened.Scale==updated.Scale && reopened.X==updated.X && reopened.Y==updated.Y,"project reopens with the size and position edited in preview");
            var bytes=await File.ReadAllBytesAsync(photo.SourcePath);
            Check(original.SequenceEqual(bytes) && window.Editing.Project.Clips.All(c=>timing[c.Id]==(c.Start,c.Duration,c.SourceOffset)),"preview resizing preserves original files and timeline timing");
            var engine=new VideoEngine(); var exported=Path.Combine(directory,"preview-resized.mp4");
            await new MultitrackEngine(engine).ExportAsync(window.Editing.Project,exported,new Progress(),CancellationToken.None);
            var frame=Path.Combine(directory,"preview-resized-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","0.75","-i",exported,"-frames:v","1",frame);
            var bitmap=new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(frame)),PixelFormats.Bgra32,null,0);
            var bounds=Bounds(photo.Id); var inside=new byte[4]; var outside=new byte[4]; var y=(int)(bounds.Top+bounds.Height/2);
            bitmap.CopyPixels(new Int32Rect((int)bounds.Left+4,y,1,1),inside,4,0); bitmap.CopyPixels(new Int32Rect((int)bounds.Left-4,y,1,1),outside,4,0);
            Check(inside[2]>200 && inside[0]<40 && outside[0]>200 && outside[2]<40,"exported image boundaries match resizing in the preview");
        }
        finally { typeof(MainWindow).GetField("dirty",flags)!.SetValue(window,false); window.Close(); }
    }
}
