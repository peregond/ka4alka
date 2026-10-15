using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Kachalka;

// Grey placeholders shown while a shelf, the catalog grid, the banner, the cast, the synopsis or the
// releases are still loading. They reserve the exact shape of the coming content, line for line, so nothing
// below them moves when the real cards arrive, and a soft light runs over them from left to right (see
// ShimmerWave). There is no timer behind them.
public partial class MainWindow
{
    // The catalog grid showed placeholders on the previous render; its cards then fade in.
    bool catalogSkeletonShown;
    // A slow pulse for a single line (the release search bar). Off in smoke tests, in Lite mode and when Windows
    // animations are turned off; the placeholder then stays still.
    static T Breathe<T>(T placeholder) where T:UIElement
    {
        if(Motion)placeholder.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(1,.5,TimeSpan.FromSeconds(1.4))
            {AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever,EasingFunction=new System.Windows.Media.Animation.SineEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut}});
        return placeholder;
    }
    // One wave for the posters whose covers are loading; rebuilt after a theme change so it picks up the new colours.
    ShimmerWave? posterWave;
    // A catalog poster whose cover is still on its way wears the shimmer instead of its colour, so a shelf that fills
    // up card by card shows which films are still loading. The shimmer goes as soon as the cover arrives, fails or the
    // card leaves the visible area; posters without a cover address keep their colour.
    void SyncPosterShimmer(Image image,CoverRequest state)
    {
        if(image.Parent is not Panel panel)return;
        Border? overlay=null;foreach(var child in panel.Children)if(child is Border {Name:"PosterShimmer"} found){overlay=found;break;}
        if(overlay==null)return;
        if(image.DataContext is MediaItem media&&image.Source==null&&state.Request!=null&&!closing&&!closed)(posterWave??=new(Motion)).Show(overlay,Math.Abs(media.Id)%6);
        else if(posterWave!=null)posterWave.Hide(overlay);else overlay.Visibility=Visibility.Collapsed;
    }
    // Real content that replaces a placeholder fades in over a quarter of a second instead of snapping in.
    // One short opacity animation on the container, not on each card; skipped with motion off.
    static void Reveal(UIElement content)
    {
        if(!Motion)return;
        content.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(260))
            {FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop,EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}});
    }
    // Three lines in place of the synopsis while the film's description is on its way.
    static FrameworkElement SynopsisSkeleton()
    {
        var wave=new ShimmerWave(Motion);
        var lines=new StackPanel{Name="DetailSynopsisSkeleton",IsHitTestVisible=false,Margin=new(0,4,0,4)};AutomationProperties.SetName(lines,"Загружаем описание");
        var lane=0;
        foreach(var (right,opacity) in new[]{(0d,1d),(40d,1d),(160d,.7d)})lines.Children.Add(SkeletonBar(13,new(0,0,right,10),opacity,wave,lane++,"BannerChip"));
        return wave.Run(lines);
    }
    // Release rows while the first source has not answered yet.
    static FrameworkElement ReleaseSkeleton()
    {
        var wave=new ShimmerWave(Motion);
        var rows=new StackPanel{Name="ReleaseSkeleton",IsHitTestVisible=false};AutomationProperties.SetName(rows,"Ищем раздачи");
        for(var index=0;index<3;index++)
        {
            var row=new Border{CornerRadius=new(16),Padding=new(20,18,20,18),Margin=new(0,0,0,10),BorderThickness=new(1)};row.SetResourceReference(Border.BackgroundProperty,"Panel");row.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
            var lines=new StackPanel();lines.Children.Add(SkeletonBar(14,new(0,0,index==1?220:120,12),1,wave,index));lines.Children.Add(SkeletonBar(10,new(0,0,index==2?300:260,0),.7,wave,index));row.Child=lines;rows.Children.Add(row);
        }
        return wave.Run(rows);
    }
    static Border SkeletonBar(double height,Thickness margin,double opacity=1,ShimmerWave? wave=null,int lane=0,string resource="Raised")
    {
        var bar=new Border{Height=height,CornerRadius=new(height/2),Margin=margin,Opacity=opacity};
        if(wave!=null)wave.Paint(bar,lane,resource);else bar.SetResourceReference(Border.BackgroundProperty,resource);
        return bar;
    }
    // How much shorter each card's lines are than the card, so the titles do not look stamped from one mould.
    static readonly double[] TitleTrim=[14,34,6,48],TitleTail=[74,58,96,66],MetaTrim=[100,84,116,92];
    // Poster cards in as many columns as the real grid would use at this width.
    FrameworkElement PosterSkeleton(int rows,string name)
    {
        // Games show wide capsules, so their placeholders use the capsule columns and shape.
        var games=section=="Игры";
        Func<double,int> columnsFor=games?WindowSizing.GameColumns:WindowSizing.PosterColumns;
        var grid=new UniformGrid{Name=name,Columns=Math.Max(1,columnsFor(Math.Max(0,Body.ActualWidth))),Margin=new(0,5,-16,0),IsHitTestVisible=false};
        AutomationProperties.SetName(grid,"Загружаем подборку");
        var wave=new ShimmerWave(Motion);
        void Fill()
        {
            grid.Children.Clear();
            for(var index=0;index<grid.Columns*rows;index++)
            {
                // The light runs through a column at once and reaches the next column a moment later.
                var lane=index%grid.Columns;
                var cell=new StackPanel{Margin=new(0,0,16,18)};
                var poster=new Border{CornerRadius=new(12)};wave.Paint(poster,lane);
                if(games)poster.SizeChanged+=GamePosterSized;else poster.SizeChanged+=PosterResized;cell.Children.Add(poster);
                // The lines take exactly the room of a real card's text (two 18 px title lines, then the 16 px line under them).
                var title=new StackPanel{Margin=new(2,10,2,0),Height=36};
                title.Children.Add(SkeletonBar(12,new(0,3,TitleTrim[index%4],3),1,wave,lane));
                title.Children.Add(SkeletonBar(12,new(0,3,TitleTail[index%4],3),.8,wave,lane));
                cell.Children.Add(title);
                cell.Children.Add(SkeletonBar(10,new(2,5,MetaTrim[index%4],3),.6,wave,lane));
                grid.Children.Add(cell);
            }
        }
        Fill();wave.Run(grid);
        // Only a real resize regroups the cards: a scroll bar appearing or disappearing (a few pixels) must not,
        // or the new height would toggle the scroll bar again and the layout would never settle.
        var measured=0d;
        grid.SizeChanged+=(_,e)=>
        {
            if(!e.WidthChanged||Math.Abs(e.NewSize.Width-measured)<48)return;measured=e.NewSize.Width;
            var columns=Math.Max(1,columnsFor(e.NewSize.Width-16));if(columns==grid.Columns)return;grid.Columns=columns;Fill();
        };
        return grid;
    }
    FrameworkElement BannerSkeleton()
    {
        var frame=new Border{Name="FeatureSkeleton",CornerRadius=new(22),MinHeight=300,Margin=new(0,0,0,28),Padding=new(36,32,36,32),IsHitTestVisible=false};frame.SetResourceReference(Border.BackgroundProperty,"Panel");
        AutomationProperties.SetName(frame,"Загружаем подборку");
        // The big frame stays flat; the light runs over the title, the line under it and the button.
        var wave=new ShimmerWave(Motion);
        var lines=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=520,HorizontalAlignment=HorizontalAlignment.Left};frame.Child=lines;
        var title=SkeletonBar(44,new(0,0,0,16),1,wave,0);title.Width=420;title.HorizontalAlignment=HorizontalAlignment.Left;lines.Children.Add(title);
        var meta=SkeletonBar(14,new(0,0,0,22),.7,wave,1);meta.Width=220;meta.HorizontalAlignment=HorizontalAlignment.Left;lines.Children.Add(meta);
        var action=new Border{Width=150,Height=52,CornerRadius=new(14),HorizontalAlignment=HorizontalAlignment.Left};wave.Paint(action,2);lines.Children.Add(action);
        return wave.Run(frame);
    }
    // Round portraits with a name line, matching the participant tiles.
    FrameworkElement PeopleSkeleton()
    {
        var wave=new ShimmerWave(Motion);
        var grid=new UniformGrid{Name="CinemaPeopleSkeleton",Columns=6,Margin=new(0,0,-8,0),IsHitTestVisible=false};AutomationProperties.SetName(grid,"Загружаем участников");
        for(var index=0;index<6;index++)
        {
            var cell=new StackPanel{Margin=new(0,0,8,8),HorizontalAlignment=HorizontalAlignment.Center};
            var portrait=new Border{Width=96,Height=120,CornerRadius=new(11),Margin=new(0,10,0,10),HorizontalAlignment=HorizontalAlignment.Center};wave.Paint(portrait,index);cell.Children.Add(portrait);
            var name=SkeletonBar(11,new(0,0,0,6),1,wave,index);name.Width=84;cell.Children.Add(name);var role=SkeletonBar(9,new(0),.7,wave,index);role.Width=52;cell.Children.Add(role);grid.Children.Add(cell);
        }
        var measured=0d;
        grid.SizeChanged+=(_,e)=>{if(!e.WidthChanged||Math.Abs(e.NewSize.Width-measured)<48)return;measured=e.NewSize.Width;var columns=Math.Clamp((int)Math.Floor(e.NewSize.Width/124),1,6);if(grid.Columns!=columns)grid.Columns=columns;};
        return wave.Run(grid);
    }
}
