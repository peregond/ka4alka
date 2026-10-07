using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Kachalka;

public partial class MainWindow
{
    sealed class ToggleContextMenu:ContextMenu
    {
        public Button? OwnerButton {get;set;}
        public bool DismissedOnOwner {get;set;}
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            // Closed can arrive after the fade animation, when the mouse is
            // already released. IsOpen changes at the actual dismissal instead.
            if(e.Property==IsOpenProperty&&Equals(e.NewValue,false)&&OwnerButton is {} owner)
            {
                var point=Mouse.GetPosition(owner);
                DismissedOnOwner=Mouse.LeftButton==MouseButtonState.Pressed&&new Rect(0,0,owner.ActualWidth,owner.ActualHeight).Contains(point);
            }
            base.OnPropertyChanged(e);
        }
    }
    static void AttachMenuToggle(Button owner,ToggleContextMenu menu)
    {
        menu.OwnerButton=owner;menu.Style=(Style)Application.Current.FindResource(typeof(ContextMenu));owner.ContextMenu=menu;
        owner.Click+=(_,_)=>
        {
            if(menu.DismissedOnOwner){menu.DismissedOnOwner=false;return;}
            menu.IsOpen=!menu.IsOpen;
        };
        owner.PreviewKeyDown+=(_,_)=>menu.DismissedOnOwner=false;
        owner.MouseLeave+=(_,_)=>{if(Mouse.LeftButton==MouseButtonState.Released)menu.DismissedOnOwner=false;};
    }
}
