using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    void ShowDownloadDiagnostics(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.Tag is not DownloadItem item)return;
        DiagnosticLog.Write("download-diagnostics-open",new{item.Id,item.ReleaseSource});
        CreateDownloadDiagnostics(item).ShowDialog();
    }

    internal Window CreateDownloadDiagnostics(DownloadItem item,Func<DownloadDiagnosticSnapshot>? snapshotProvider=null,Func<Task>? retryConnections=null,Func<Task>? resumeDownload=null)
    {
        var work=SystemParameters.WorkArea;
        var window=new Window
        {
            Owner=this,Title="Почему не скачивается? · "+item.DisplayName,ShowInTaskbar=false,
            Width=Math.Clamp(ActualWidth-40,320,640),Height=Math.Clamp(ActualHeight-40,300,680),
            MinWidth=320,MinHeight=280,MaxWidth=Math.Max(320,work.Width-24),MaxHeight=Math.Max(280,work.Height-24),
            WindowStartupLocation=WindowStartupLocation.CenterOwner,UseLayoutRounding=true,
            FontFamily=FontFamily,FontSize=13
        };
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Text");
        var root=new DockPanel{Margin=new(20)};window.Content=root;
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var copy=ActionButton("Скопировать диагностику","IconFile",()=>{});
        copy.Name="CopyDownloadDiagnosis";copy.Margin=new(0,0,6,6);copy.Padding=new(10,7,10,7);
        AutomationProperties.SetName(copy,"Скопировать диагностику загрузки");footer.Children.Add(copy);
        var close=Button("Закрыть",()=>window.Close());close.Style=(Style)FindResource("QuietButton");close.IsCancel=true;
        close.Margin=new(0,0,0,6);close.Padding=new(12,7,12,7);AutomationProperties.SetName(close,"Закрыть диагностику");footer.Children.Add(close);
        var scroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(scroll,"Диагностика загрузки");root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        var heading=Text("Почему не скачивается?",22);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        var title=Text(item.DisplayName,13,true);title.Margin=new(0,0,0,14);body.Children.Add(title);
        var diagnosisHost=new Border{Padding=new(14),CornerRadius=new(12),BorderThickness=new(1),Margin=new(0,0,0,16)};
        diagnosisHost.SetResourceReference(Border.BackgroundProperty,"Panel");diagnosisHost.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");body.Children.Add(diagnosisHost);
        var diagnosisBody=new StackPanel();diagnosisHost.Child=diagnosisBody;
        var reason=Text("",17);reason.FontWeight=FontWeights.SemiBold;reason.Name="DownloadDiagnosisReason";
        var explanation=Text("",13);var recommendation=Text("",13,true);
        diagnosisBody.Children.Add(reason);diagnosisBody.Children.Add(explanation);diagnosisBody.Children.Add(recommendation);
        var actions=new WrapPanel();diagnosisBody.Children.Add(actions);
        var notice=Text("",12,true);notice.Name="DownloadDiagnosisNotice";notice.Margin=new(0,8,0,0);diagnosisBody.Children.Add(notice);
        var provider=snapshotProvider??(()=>downloads.GetDiagnostics(item));
        var snapshot=provider();
        bool actionBusy=false;
        var retry=new Button{Content="Повторить соединение"};
        retry.Name="RetryDownloadConnections";retry.Style=(Style)FindResource("PrimaryButton");retry.Margin=new(0,4,6,0);retry.Padding=new(10,7,10,7);actions.Children.Add(retry);
        AutomationProperties.SetName(retry,"Повторить соединение загрузки");
        var resume=new Button{Content="Продолжить"};resume.Name="ResumeDiagnosedDownload";resume.Style=(Style)FindResource("PrimaryButton");resume.Margin=new(0,4,6,0);resume.Padding=new(10,7,10,7);actions.Children.Add(resume);
        AutomationProperties.SetName(resume,"Продолжить загрузку из диагностики");
        var folder=ActionButton("Папка","IconFolder",()=>OpenFolder(new Button{Tag=item},new RoutedEventArgs()));
        folder.IsEnabled=!string.IsNullOrWhiteSpace(item.Folder);folder.Margin=new(0,4,6,0);folder.Padding=new(10,7,10,7);actions.Children.Add(folder);
        var other=ActionButton("Другая раздача","IconMovies",()=>{window.Close();OpenDownloadCard(new Button{Tag=item},new RoutedEventArgs());});
        other.Visibility=item.HasMediaCard?Visibility.Visible:Visibility.Collapsed;other.Margin=new(0,4,6,0);other.Padding=new(10,7,10,7);actions.Children.Add(other);
        AutomationProperties.SetName(other,"Выбрать другую раздачу");

        var facts=new StackPanel();body.Children.Add(facts);
        TextBlock Fact(string label)
        {
            var labelText=Text(label,12,true);labelText.Margin=new(0,0,0,3);facts.Children.Add(labelText);
            var value=Text("",13);value.Margin=new(0,0,0,12);facts.Children.Add(value);return value;
        }
        var network=Fact("Подключение к сети");
        var metadata=Fact("Список файлов");
        var peers=Fact("Участники этой раздачи");
        var disk=Fact("Свободное место");
        var trackersHeading=Text("Трекеры · последние ответы",13);trackersHeading.FontWeight=FontWeights.Medium;body.Children.Add(trackersHeading);
        var trackers=new StackPanel{Margin=new(0,0,0,12)};body.Children.Add(trackers);
        var trackerFingerprint="";
        var firewallHost=new Expander{Header="О подключениях и брандмауэре",Margin=new(0,4,0,4)};body.Children.Add(firewallHost);
        var firewallBody=new StackPanel{Margin=new(0,10,0,0)};firewallHost.Content=firewallBody;
        firewallBody.Children.Add(Text("Закрытые входящие подключения могут уменьшать число доступных участников. При этом торрент может скачиваться через исходящие подключения; отсутствие ответа трекера не доказывает проблему брандмауэра.",12,true));
        firewallBody.Children.Add(Text("Если хочешь разрешить входящие подключения, добавим правило только для Качалки, TCP и UDP в частных сетях. Windows запросит права администратора.",12,true));
        var firewall=AsyncButton("Разрешить в брандмауэре",async()=>
        {
            try{notice.Text=await WindowsIntegration.AllowFirewallAsync()?"Исключение добавлено. Можно повторить соединение.":"Запрос отменён. Параметры брандмауэра не изменены.";}
            catch(Exception error){notice.Text=Redacted(error.Message);ErrorLog.Write(error);}
        });firewall.HorizontalAlignment=HorizontalAlignment.Left;firewall.Padding=new(10,7,10,7);firewallBody.Children.Add(firewall);
        AutomationProperties.SetName(firewall,"Разрешить входящие подключения Качалки");
        copy.Click+=(_,_)=>
        {
            try{Clipboard.SetText(DownloadDiagnostics.CreateReport(snapshot,item,prefs));notice.Text="Диагностика скопирована. Личные папки и ключи доступа скрыты.";}
            catch(Exception error){notice.Text="Не удалось скопировать: "+error.Message;}
        };
        string Redacted(string value)=>DiagnosticReport.Redact(value,new[]{item.Folder,prefs.Folder,Preferences.DataDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)});
        bool working=false;
        void Refresh()
        {
            if(working)return;
            working=true;
            try
            {
                snapshot=provider();var diagnosis=DownloadDiagnostics.Explain(snapshot);
                reason.Text=diagnosis.Title;reason.SetResourceReference(TextBlock.ForegroundProperty,diagnosis.Level==DownloadDiagnosticLevel.Attention?"Danger":"Text");
                explanation.Text=Redacted(diagnosis.Explanation);recommendation.Text=diagnosis.Recommendation;
                retry.Visibility=snapshot.Paused||snapshot.Completed?Visibility.Collapsed:Visibility.Visible;
                retry.IsEnabled=diagnosis.CanRetryConnections&&!item.Busy&&!actionBusy;
                resume.Visibility=snapshot.Paused?Visibility.Visible:Visibility.Collapsed;resume.IsEnabled=!item.Busy&&!actionBusy;
                network.Text=DownloadDiagnostics.NetworkFact(snapshot.NetworkAvailable);
                metadata.Text=snapshot.HasMetadata?"Список и размеры файлов получены.":"Список файлов пока не получен. Для magnet-ссылки нужны метаданные от участника.";
                peers.Text=$"Подключено: {Math.Max(0,snapshot.Connections)} · отдают: {Math.Max(0,snapshot.Seeds)}"+(snapshot.DhtEnabled.HasValue?$" · DHT: {(snapshot.DhtEnabled.Value?"включён":"выключен")}":"");
                disk.Text=Redacted(DownloadDiagnostics.SpaceFact(snapshot));
                var fingerprint=string.Join("|",snapshot.Trackers.Select(t=>$"{t.Host}/{t.Protocol}/{t.Status}/{t.Responded}/{t.Failed}"));
                if(fingerprint!=trackerFingerprint||trackers.Children.Count==0)
                {
                    trackerFingerprint=fingerprint;trackers.Children.Clear();
                    foreach(var tracker in snapshot.Trackers.Take(40))trackers.Children.Add(Text(tracker.Protocol+"://"+tracker.Host+" · "+DownloadDiagnostics.TrackerFact(tracker),12,true));
                    if(snapshot.Trackers.Count==0)trackers.Children.Add(Text("Данных об ответах трекеров пока нет. Поиск участников также зависит от настроек DHT и самой раздачи.",12,true));
                    if(snapshot.Trackers.Count>40)trackers.Children.Add(Text($"И ещё {snapshot.Trackers.Count-40}. Полный список есть в скопированной диагностике.",12,true));
                }
            }
            catch(Exception error){notice.Text="Не удалось обновить проверку: "+Redacted(error.Message);}
            finally{working=false;}
        }
        retry.Click+=async(_,_)=>
        {
            if(actionBusy)return;actionBusy=true;Refresh();
            try
            {
                bool changed;
                if(retryConnections!=null){await retryConnections();changed=true;}
                else changed=await downloads.RetryConnectionsAsync(item);
                notice.Text=changed?"Поиск участников обновлён. Дай соединениям немного времени.":item.Paused?"Загрузка остаётся на паузе. Для запуска нажми «Продолжить».":"Соединения не изменены. Проверь текущее состояние сети и задачи выше.";
                DiagnosticLog.Write("download-diagnostics-retry",new{item.Id,item.Paused,Changed=changed});
            }
            catch(Exception error){notice.Text="Не удалось повторить соединение: "+Redacted(error.Message);ErrorLog.Write(error);}
            finally{actionBusy=false;Refresh();SyncTimer();RefreshDownloadView();}
        };
        resume.Click+=async(_,_)=>
        {
            if(actionBusy)return;actionBusy=true;Refresh();
            try{if(resumeDownload!=null)await resumeDownload();else if(item.Paused)await downloads.Toggle(item);notice.Text=item.Paused?"Загрузка остаётся на паузе. Проверь свободное место и сообщение выше.":"Загрузка продолжена.";}
            catch(Exception error){notice.Text="Не удалось продолжить: "+Redacted(error.Message);ErrorLog.Write(error);}
            finally{actionBusy=false;Refresh();SyncTimer();RefreshDownloadView();}
        };
        Refresh();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        timer.Tick+=(_,_)=>Refresh();window.Loaded+=(_,_)=>timer.Start();window.Closed+=(_,_)=>timer.Stop();
        return window;
    }
}
