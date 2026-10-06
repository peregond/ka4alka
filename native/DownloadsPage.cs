using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Kachalka;
public partial class MainWindow
{
    void RenderDownloads()
    {
        downloads.Update();
        var actions=new WrapPanel{Margin=new(0,0,0,8)};PageHeader.Children.Add(actions);
        foreach(var command in new[]{("Начать загрузки",false,false),("Остановить загрузки",false,true),("Запустить раздачи",true,false),("Остановить раздачи",true,true)})
        {
            var button=AsyncButton(command.Item1,async()=>{var count=await downloads.SetGroupAsync(command.Item2,command.Item3);Status.Text=$"Задач изменено: {count}";SyncTimer();});
            button.ToolTip=command.Item2?"Только завершённые торренты":"Только незавершённые загрузки";button.Padding=new(10,6,10,6);button.IsEnabled=downloads.Items.Count>0;AutomationProperties.SetName(button,command.Item1);actions.Children.Add(button);
        }
        var limits=new WrapPanel{Margin=new(0,0,0,12),VerticalAlignment=VerticalAlignment.Center};PageHeader.Children.Add(limits);
        TextBox Field(string label,int value)
        {
            var text=Text(label,12,true);text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new(0,0,6,0);limits.Children.Add(text);
            var field=new TextBox{Text=value.ToString(),Width=76,Padding=new(7,5,7,5),Margin=new(0,0,12,0)};AutomationProperties.SetName(field,label);limits.Children.Add(field);return field;
        }
        var down=Field("Загрузка, КБ/с",downloads.DownloadLimitKbps);var up=Field("Отдача, КБ/с",downloads.UploadLimitKbps);
        limits.Children.Add(AsyncButton("Применить",async()=>
        {
            if(!int.TryParse(down.Text,out var d)||!int.TryParse(up.Text,out var u)||d<0||u<0||d>int.MaxValue/1024||u>int.MaxValue/1024){Status.Text="Укажи целую скорость от 0 до 2097151 КБ/с. 0 — без ограничения.";return;}
            var oldD=downloads.DownloadLimitKbps;var oldU=downloads.UploadLimitKbps;
            await downloads.SetLimitsAsync(d,u);prefs.MaxDownloadKbps=d;prefs.MaxUploadKbps=u;
            try{prefs.Save();Status.Text="Лимиты скорости сохранены и применены ко всей очереди.";}
            catch{prefs.MaxDownloadKbps=oldD;prefs.MaxUploadKbps=oldU;await downloads.SetLimitsAsync(oldD,oldU);throw;}
        }));
        var hint=Text("0 — без ограничения. Раздачи — завершённые торренты. Удаление из очереди сохраняет файлы.",11,true);hint.Margin=new(0,0,0,12);PageHeader.Children.Add(hint);
        if(downloads.Items.Count>0){Body.Children.Add(new ListBox{ItemsSource=downloads.Items,ItemTemplate=(DataTemplate)FindResource("DownloadRow")});return;}
        var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=400};
        empty.Children.Add(Text("Загрузок пока нет",22));empty.Children.Add(Text("Добавь раздачу из карточки фильма, magnet-ссылку или .torrent-файл.",13,true));
        empty.Children.Add(Button("＋ Добавить торрент",()=>AddTorrent(this,new RoutedEventArgs())));Body.Children.Add(empty);
    }
    void DownloadDetails(object sender,RoutedEventArgs e)
    {
        var item=(DownloadItem)((Button)sender).Tag;downloads.Update();
        var window=new Window{Title="Файлы · "+item.Name,Owner=this,Width=Math.Min(740,Math.Max(340,ActualWidth-30)),Height=Math.Min(550,Math.Max(280,ActualHeight-30)),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.SetResourceReference(Window.BackgroundProperty,"Bg");window.SetResourceReference(Window.ForegroundProperty,"Text");
        var panel=new DockPanel{Margin=new(20)};window.Content=panel;
        var heading=Text(item.Name,18);heading.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
        var status=Text("",12,true);DockPanel.SetDock(status,Dock.Top);panel.Children.Add(status);
        var template=new DataTemplate(typeof(DownloadFile));var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(FrameworkElement.MarginProperty,new Thickness(0,6,0,8));
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);row.AppendChild(name);
        var progress=new FrameworkElementFactory(typeof(ProgressBar));progress.SetValue(ProgressBar.MaximumProperty,100d);progress.SetValue(FrameworkElement.HeightProperty,4d);progress.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,4));progress.SetBinding(ProgressBar.ValueProperty,new Binding("Progress"));row.AppendChild(progress);
        var summary=new FrameworkElementFactory(typeof(TextBlock));summary.SetBinding(TextBlock.TextProperty,new Binding("Summary"));summary.SetValue(TextBlock.FontSizeProperty,11d);row.AppendChild(summary);template.VisualTree=row;
        var list=new ListBox{ItemTemplate=template};AutomationProperties.SetName(list,"Файлы загрузки");panel.Children.Add(list);
        void Refresh(){downloads.Update();list.ItemsSource=item.Files.ToArray();status.Text=item.Files.Count==0?"Список файлов появится после получения метаданных раздачи.":$"Готово файлов: {item.Files.Count(f=>f.Progress>=100)} из {item.Files.Count}. Частично скачанные файлы показаны с прогрессом.";}
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=(_,_)=>Refresh();window.Closed+=(_,_)=>timer.Stop();Refresh();timer.Start();window.ShowDialog();
    }
    async void DeleteDownloadFiles(object sender,RoutedEventArgs e)
    {
        var item=(DownloadItem)((Button)sender).Tag;
        if(MessageBox.Show(this,"Убрать «"+item.Name+"» из очереди и удалить её скачанные и частичные файлы?\n\nЭто действие нельзя отменить. Другие файлы в папке останутся.","Удалить загрузку и файлы",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        try{await downloads.Remove(item,true);Render();Status.Text="Задача и её файлы удалены.";}catch(Exception error){Status.Text="Не удалось удалить файлы: "+error.Message;}finally{SyncTimer();}
    }
}
