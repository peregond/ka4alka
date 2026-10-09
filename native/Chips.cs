using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Kachalka;

// Filter chips: a 40 px pill with an optional leading icon, a caption and a trailing chevron.
// The active state is a plain brush swap (AccentSoft fill, AccentLine edge, Accent caption).
public partial class MainWindow
{
    FrameworkElement ChipContent(string text,bool chevron=true,string? icon=null)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        if(icon!=null)
        {
            var glyph=new Path{Data=(Geometry)FindResource(icon),Width=16,Height=16,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};
            glyph.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});row.Children.Add(glyph);
        }
        row.Children.Add(new TextBlock{Text=text,Tag="ChipText",VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
        if(chevron)
        {
            var arrow=new Path{Data=(Geometry)FindResource("IconChevronDown"),Width=16,Height=16,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(6,0,-4,0),VerticalAlignment=VerticalAlignment.Center};
            arrow.SetResourceReference(Shape.StrokeProperty,"Subtle");row.Children.Add(arrow);
        }
        return row;
    }
    static TextBlock? ChipLabel(Button button)=>button.Content is Panel panel?panel.Children.OfType<TextBlock>().FirstOrDefault(x=>Equals(x.Tag,"ChipText")):null;
    internal static string ChipText(Button button)=>ChipLabel(button)?.Text??button.Content as string??"";
    static void SetChipText(Button button,string text){if(ChipLabel(button) is {} label)label.Text=text;else button.Content=text;}
    void StyleChip(Button button,bool active)
    {
        button.SetResourceReference(Control.BackgroundProperty,active?"AccentSoft":"Panel");
        button.SetResourceReference(Control.BorderBrushProperty,active?"AccentLine":"EdgeSoft");
        button.SetResourceReference(Control.ForegroundProperty,active?"Accent":"TextSoft");
        button.FontWeight=active?FontWeights.SemiBold:FontWeights.Medium;
    }
}
