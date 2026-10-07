using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Kachalka;

public partial class MainWindow
{
    static void AttachMenuToggle(Button owner,ContextMenu menu)
    {
        owner.ContextMenu=menu;
        bool ownerDismissed=false;
        // WPF releases the popup's capture and closes it on mouse-down before
        // the underlying Button.Click arrives on mouse-up. Remember that exact
        // dismissal instead of treating the later click as a request to reopen.
        menu.Closed+=(_,_)=>
        {
            var point=Mouse.GetPosition(owner);
            ownerDismissed=Mouse.LeftButton==MouseButtonState.Pressed&&new Rect(0,0,owner.ActualWidth,owner.ActualHeight).Contains(point);
        };
        owner.Click+=(_,_)=>
        {
            if(ownerDismissed){ownerDismissed=false;return;}
            menu.IsOpen=!menu.IsOpen;
        };
        owner.PreviewKeyDown+=(_,_)=>ownerDismissed=false;
        owner.MouseLeave+=(_,_)=>{if(Mouse.LeftButton==MouseButtonState.Released)ownerDismissed=false;};
    }
}
