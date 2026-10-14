using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
namespace ScreenStudio.UI;

// Vendored Lucide 0.468.0 vectors, rendered by WPF without a browser or SVG runtime.
public sealed class LucideIcon : Control
{
    private static readonly Dictionary<string,Geometry> Cache = [];
    private readonly Geometry geometry;
    public LucideIcon(string name,double size)
    {
        Width = Height = size; IsHitTestVisible = false; Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        lock (Cache)
        {
            if (!Cache.TryGetValue(name,out var found))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ScreenStudio.Assets.Icons.Lucide.{name}.svg") ?? throw new InvalidOperationException($"Icono Lucide no encontrado: {name}");
                var root = XDocument.Load(stream).Root!; var group = new GeometryGroup();
                foreach (var node in root.Elements())
                {
                    double N(string key,double fallback = 0) => double.TryParse(node.Attribute(key)?.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n) ? n : fallback;
                    Geometry? shape = node.Name.LocalName switch
                    {
                        "path" => Geometry.Parse(node.Attribute("d")!.Value),
                        "line" => new LineGeometry(new Point(N("x1"),N("y1")),new Point(N("x2"),N("y2"))),
                        "circle" => new EllipseGeometry(new Point(N("cx"),N("cy")),N("r"),N("r")),
                        "ellipse" => new EllipseGeometry(new Point(N("cx"),N("cy")),N("rx"),N("ry")),
                        "rect" => new RectangleGeometry(new Rect(N("x"),N("y"),N("width"),N("height")),N("rx"),N("ry",N("rx"))),
                        "polyline" or "polygon" => Geometry.Parse("M " + node.Attribute("points")!.Value + (node.Name.LocalName == "polygon" ? " Z" : "")),
                        _ => null
                    };
                    if (shape != null) group.Children.Add(shape);
                }
                group.Freeze(); found = group; Cache[name] = found;
            }
            geometry = found;
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));
        dc.DrawGeometry(null,new Pen(Foreground,1.8) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round },geometry);
        dc.Pop();
    }
}
