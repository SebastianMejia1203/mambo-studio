using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace ScreenStudio.UI;

public static class StudioUi
{
    private static readonly Dictionary<string,string> Icons = new()
    {
        ["plus"]="plus", ["grid"]="layout-grid", ["list"]="list", ["camera"]="camera", ["microphone"]="mic",
        ["new"]="file-plus-2", ["open"]="folder-open", ["save"]="save", ["import"]="import", ["export"]="upload",
        ["select"]="mouse-pointer-2", ["trim"]="crop", ["split"]="split", ["cut"]="scissors", ["copy"]="copy",
        ["paste"]="clipboard-paste", ["delete"]="trash-2", ["undo"]="undo-2", ["redo"]="redo-2",
        ["markin"]="panel-left-close", ["markout"]="panel-right-close", ["reset"]="rotate-ccw",
        ["text"]="type", ["audio"]="music", ["video"]="video", ["image"]="image", ["fit"]="maximize",
        ["zoomin"]="zoom-in", ["zoomout"]="zoom-out", ["play"]="play", ["pause"]="pause", ["stop"]="square",
        ["record"]="circle", ["start"]="skip-back", ["end"]="skip-forward", ["back"]="rewind", ["forward"]="fast-forward",
        ["left"]="chevron-left", ["right"]="chevron-right", ["close"]="x", ["settings"]="settings-2",
        ["check"]="check", ["files"]="files", ["inspector"]="sliders-horizontal", ["canvas"]="palette",
        ["collapse"]="panel-left-close", ["eye"]="eye", ["snap"]="magnet", ["panelleft"]="panel-left",
        ["panelright"]="panel-right", ["move"]="arrow-left-right", ["more"]="ellipsis", ["search"]="search", ["cursorimage"]="image-plus"
    };
    public static LucideIcon Icon(string name,double size = 16) => new(Icons.GetValueOrDefault(name,"circle"),size);
    public static Button Button(string icon, string name, Action action)
    {
        var button = new Button { Content = Icon(icon), ToolTip = name, HorizontalAlignment = HorizontalAlignment.Left, Width = 32, Height = 32, Padding = new Thickness(7) };
        AutomationProperties.SetName(button,name); ToolTipService.SetInitialShowDelay(button,300); ToolTipService.SetShowOnDisabled(button,true); button.Click += (_,_) => action(); return button;
    }
    public static void SetButtonLabel(Button button,string icon,string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Icon(icon));
        content.Children.Add(new TextBlock { Text=text, Margin=new Thickness(7,0,0,0), VerticalAlignment=VerticalAlignment.Center });
        button.Content=content; button.Width=double.NaN; button.Padding=new Thickness(10,6,10,6);
    }
    public static Button ActionButton(string icon,string text,string tooltip,Action action)
    {
        var button = Button(icon,tooltip,action); SetButtonLabel(button,icon,text);
        button.SetResourceReference(Control.BackgroundProperty,"Raised");
        button.SetResourceReference(Control.BorderBrushProperty,"Border"); return button;
    }
    public static WrapPanel ActionRow(Panel parent)
    {
        var row = new WrapPanel { Margin=new Thickness(-2,8,0,4) }; parent.Children.Add(row); return row;
    }
    public static TextBlock Label(string text, string brush = "Text", double size = 12)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, brush); return label;
    }
    public static Border Divider() { var border = new Border { Width = 1, Margin = new Thickness(8, 5, 8, 5) }; border.SetResourceReference(Border.BackgroundProperty, "Border"); return border; }
    public static StackPanel Field(Panel parent, string name, UIElement input)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; var label = Label(name, "Muted", 11); label.Margin = new Thickness(0, 0, 0, 5); field.Children.Add(label); field.Children.Add(input); parent.Children.Add(field); return field;
    }
    public static void ApplyTheme(bool dark)
    {
        var keys = new[] { "Background", "Surface", "Raised", "Border", "Text", "Muted", "Accent", "AccentSoft", "Danger", "ClipVideo", "ClipImage", "ClipText", "ClipAudio", "ClipTextInk" };
        var colors = dark ? new[] { "#17191D", "#202328", "#2A2E35", "#353A43", "#E8EAF0", "#969FAB", "#A8C98D", "#354330", "#EC817A", "#354C67", "#514468", "#695B36", "#31594B", "#EDF2FA" } : new[] { "#ECEFEF", "#FAFBFC", "#E7EBED", "#D4DADF", "#253039", "#67747D", "#4A7851", "#E0EDDF", "#C44942", "#C9DFEF", "#DFD3F0", "#EEE0AE", "#C8E5DA", "#2B3942" };
        for (var i = 0; i < keys.Length; i++) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[i])!; brush.Freeze(); Application.Current.Resources[keys[i]] = brush; }
        Application.Current.Resources[SystemColors.ControlBrushKey] = Application.Current.Resources["Surface"];
        Application.Current.Resources[SystemColors.ControlTextBrushKey] = Application.Current.Resources["Text"];
    }
}



