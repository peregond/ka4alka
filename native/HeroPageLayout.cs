using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    ScrollViewer? heroPageScroll;
    StackPanel? heroPageContent;
    FrameworkElement? heroPageHost;

    void UseHeroPageLayout(ScrollViewer scroll,StackPanel content,FrameworkElement? hero)
    {
        if(hero==null)return;
        heroPageScroll=scroll;heroPageContent=content;heroPageHost=hero;
        UpdateHeroPageLayout();
    }
    void UpdateHeroPageLayout()
    {
        if(heroPageScroll==null||heroPageContent==null||heroPageHost==null||!Body.Children.Contains(heroPageScroll))return;
        var gutter=CenterRegion.Margin.Left;
        // The native scrollbar owns its own width; no hard-coded scrollbar estimate.
        heroPageScroll.Margin=new(-gutter,0,-gutter,0);
        heroPageContent.Margin=new(gutter,0,gutter,0);
        var bottom=heroPageHost.Margin.Bottom;
        heroPageHost.Margin=new(-gutter,0,-gutter,bottom);
    }
}
