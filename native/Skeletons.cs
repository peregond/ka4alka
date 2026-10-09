using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Kachalka;

// Static grey placeholders shown while a shelf, the catalog grid, the banner or the cast are still loading.
// They only reserve the shape of the coming content; there is no animation or timer behind them.
public partial class MainWindow
{
    static Border SkeletonBar(double height,Thickness margin,double opacity=1)
    {
        var bar=new Border{Height=height,CornerRadius=new(height/2),Margin=margin,Opacity=opacity};bar.SetResourceReference(Border.BackgroundProperty,"Raised");return bar;
    }
    // Poster cards in as many columns as the real grid would use at this width.
    FrameworkElement PosterSkeleton(int rows,string name)
    {
        var grid=new UniformGrid{Name=name,Columns=Math.Max(1,WindowSizing.PosterColumns(Math.Max(0,Body.ActualWidth))),Margin=new(0,5,-16,0),IsHitTestVisible=false};
        AutomationProperties.SetName(grid,"Загружаем подборку");
        void Fill()
        {
            grid.Children.Clear();
            for(var index=0;index<grid.Columns*rows;index++)
            {
                var cell=new StackPanel{Margin=new(0,0,16,18)};
                var poster=new Border{CornerRadius=new(12)};poster.SetResourceReference(Border.BackgroundProperty,"Raised");poster.SizeChanged+=PosterResized;cell.Children.Add(poster);
                cell.Children.Add(SkeletonBar(12,new(2,12,28,0)));cell.Children.Add(SkeletonBar(10,new(2,10,64,0),.7));grid.Children.Add(cell);
            }
        }
        Fill();
        // Only a real resize regroups the cards: a scroll bar appearing or disappearing (a few pixels) must not,
        // or the new height would toggle the scroll bar again and the layout would never settle.
        var measured=0d;
        grid.SizeChanged+=(_,e)=>
        {
            if(!e.WidthChanged||Math.Abs(e.NewSize.Width-measured)<48)return;measured=e.NewSize.Width;
            var columns=Math.Max(1,WindowSizing.PosterColumns(e.NewSize.Width-16));if(columns==grid.Columns)return;grid.Columns=columns;Fill();
        };
        return grid;
    }
    FrameworkElement BannerSkeleton()
    {
        var frame=new Border{Name="FeatureSkeleton",CornerRadius=new(22),MinHeight=300,Margin=new(0,0,0,28),Padding=new(36,32,36,32),IsHitTestVisible=false};frame.SetResourceReference(Border.BackgroundProperty,"Panel");
        AutomationProperties.SetName(frame,"Загружаем подборку");
        var lines=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=520,HorizontalAlignment=HorizontalAlignment.Left};frame.Child=lines;
        var title=SkeletonBar(44,new(0,0,0,16));title.Width=420;title.HorizontalAlignment=HorizontalAlignment.Left;lines.Children.Add(title);
        var meta=SkeletonBar(14,new(0,0,0,22),.7);meta.Width=220;meta.HorizontalAlignment=HorizontalAlignment.Left;lines.Children.Add(meta);
        var action=new Border{Width=150,Height=52,CornerRadius=new(14),HorizontalAlignment=HorizontalAlignment.Left};action.SetResourceReference(Border.BackgroundProperty,"Raised");lines.Children.Add(action);
        return frame;
    }
    // Round portraits with a name line, matching the participant tiles.
    FrameworkElement PeopleSkeleton()
    {
        var grid=new UniformGrid{Name="CinemaPeopleSkeleton",Columns=6,Margin=new(0,0,-8,0),IsHitTestVisible=false};AutomationProperties.SetName(grid,"Загружаем участников");
        for(var index=0;index<6;index++)
        {
            var cell=new StackPanel{Margin=new(0,0,8,8),HorizontalAlignment=HorizontalAlignment.Center};
            var portrait=new Border{Width=88,Height=88,CornerRadius=new(44),Margin=new(0,10,0,10),HorizontalAlignment=HorizontalAlignment.Center};portrait.SetResourceReference(Border.BackgroundProperty,"Raised");cell.Children.Add(portrait);
            var name=SkeletonBar(11,new(0,0,0,6));name.Width=84;cell.Children.Add(name);var role=SkeletonBar(9,new(0),.7);role.Width=52;cell.Children.Add(role);grid.Children.Add(cell);
        }
        var measured=0d;
        grid.SizeChanged+=(_,e)=>{if(!e.WidthChanged||Math.Abs(e.NewSize.Width-measured)<48)return;measured=e.NewSize.Width;var columns=Math.Clamp((int)Math.Floor(e.NewSize.Width/124),1,6);if(grid.Columns!=columns)grid.Columns=columns;};
        return grid;
    }
}
