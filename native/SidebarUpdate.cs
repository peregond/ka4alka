using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Kachalka;
public partial class MainWindow
{
    bool updatePulseRunning;
    void RefreshSidebarUpdate()
    {
        var available=preparedUpdateJob!=null;
        SidebarUpdateButton.Visibility=available?Visibility.Visible:Visibility.Collapsed;
        SidebarUpdateButton.IsEnabled=available&&!checkingUpdate;
        SidebarUpdateButton.Content=IconLabel(compactWidth?"":"Обновить","IconRefresh");
        var version=updateOffer?.Manifest.Version;
        SidebarUpdateButton.ToolTip=available?$"Установить версию {version} и перезапустить":"Обновить приложение";
        AutomationProperties.SetName(SidebarUpdateButton,"Обновить приложение");
        if(available&&!updatePulseRunning)
        {
            updatePulseRunning=true;
            if(SystemParameters.ClientAreaAnimation)
                SidebarUpdateButton.BeginAnimation(OpacityProperty,new DoubleAnimation(.7,1,TimeSpan.FromSeconds(1.3)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        }
        else if(!available&&updatePulseRunning){SidebarUpdateButton.BeginAnimation(OpacityProperty,null);updatePulseRunning=false;}
    }
    async void SidebarUpdate(object sender,RoutedEventArgs e)=>await RestartForUpdateAsync();
}
