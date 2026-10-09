using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Kachalka;

// Status messages appear as a small notice above the content and fade out of the layout on their own,
// instead of a line that keeps an old message at the bottom of the window.
public partial class MainWindow
{
    DispatcherTimer? statusTimer;
    void InitializeStatusToast()
    {
        statusTimer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher){Interval=TimeSpan.FromSeconds(5)};
        statusTimer.Tick+=(_,_)=>{statusTimer.Stop();Status.Text="";};
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock)).AddValueChanged(Status,(_,_)=>ShowStatus());
    }
    void ShowStatus()
    {
        if(statusTimer==null)return;
        statusTimer.Stop();
        var text=Status.Text??"";
        StatusToast.Visibility=text.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(text.Length==0)return;
        // Problems stay a little longer so they can be read; the next message replaces the current one.
        var problem=text.StartsWith("Не удалось",StringComparison.Ordinal)||text.StartsWith("Ошибка",StringComparison.Ordinal)||text.Contains("недоступ",StringComparison.OrdinalIgnoreCase);
        statusTimer.Interval=TimeSpan.FromSeconds(problem?9:5);statusTimer.Start();
    }
}
