using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace Kachalka;

public partial class MainWindow
{
    internal Window CreateFirstRunSetup()
    {
        var window=new Window{ShowInTaskbar=false,Owner=this,Title="Качалка · первый запуск",Width=Math.Clamp(SystemParameters.WorkArea.Width-40,320,500),SizeToContent=SizeToContent.Height,MinWidth=320,MaxHeight=Math.Max(240,SystemParameters.WorkArea.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily,FontSize=13};
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Text");
        var panel=new StackPanel{Margin=new(24)};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("Всё готово к загрузкам",23));
        panel.Children.Add(Text("Файлы будут сохраняться здесь:",12,true));
        panel.Children.Add(Text(prefs.Folder,13));
        panel.Children.Add(Text("Брандмауэр",16));
        panel.Children.Add(Text("Разреши Качалке принимать подключения от участников раздачи. Это может помочь торрентам находить больше участников и начинать загрузку.",13,true));
        panel.Children.Add(Text("Добавим исключение только для Качалки: TCP и UDP в частных сетях. Windows попросит права администратора.",12,true));
        var notice=Text("",12,true);panel.Children.Add(notice);
        var firewall=AsyncButton("Разрешить в брандмауэре",async()=>
        {
            try{notice.Text=await WindowsIntegration.AllowFirewallAsync()?"Исключение добавлено. Можно начинать загрузки.":"Запрос отменён. Настроить брандмауэр можно позже в настройках.";}
            catch(Exception error){notice.Text=error.Message;ErrorLog.Write(error);}
        });firewall.HorizontalAlignment=HorizontalAlignment.Left;firewall.Style=(Style)FindResource("PrimaryButton");panel.Children.Add(firewall);
        panel.Children.Add(Text("Запуск вместе с Windows",16));
        var startup=new CheckBox{Content=Text("Открывать Качалку при входе в Windows"),IsChecked=WindowsIntegration.StartupEnabled,Style=(Style)FindResource("SettingsSwitch"),Margin=new(0,0,0,12)};
        startup.Click+=(_,_)=>{try{WindowsIntegration.SetStartup(startup.IsChecked==true);}catch(Exception error){startup.IsChecked=WindowsIntegration.StartupEnabled;notice.Text="Не удалось изменить автозапуск: "+error.Message;}};panel.Children.Add(startup);
        var finish=Button("Готово",()=>window.Close());finish.HorizontalAlignment=HorizontalAlignment.Right;finish.IsDefault=true;panel.Children.Add(finish);
        AutomationProperties.SetName(firewall,"Разрешить Качалку в брандмауэре");return window;
    }
    internal void EnsureFirstRunSetup()
    {
        var firstRun=!prefs.FolderConfigured;
        if(EnsureDownloadFolder()&&firstRun)CreateFirstRunSetup().ShowDialog();
    }
}
