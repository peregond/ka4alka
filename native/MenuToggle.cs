using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Kachalka;

public partial class MainWindow
{
    static void AttachMenuToggle(Button owner,ContextMenu menu)
    {
        owner.ContextMenu=menu;
        owner.Click+=(_,_)=>menu.IsOpen=!menu.IsOpen;
        // A popup captures the mouse. Consume a second press on its owner before
        // WPF dismisses the popup and forwards that press as a fresh button click.
        menu.AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent,new MouseButtonEventHandler((_,e)=>
        {
            if(e.ChangedButton!=MouseButton.Left||!menu.IsOpen)return;
            var point=Mouse.GetPosition(owner);
            if(!new Rect(0,0,owner.ActualWidth,owner.ActualHeight).Contains(point))return;
            e.Handled=true;menu.IsOpen=false;
        }),true);
    }
}
