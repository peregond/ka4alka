using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Kachalka;

public partial class MainWindow
{
    string settingsReturnSection="Фильмы";
    MediaItem? settingsReturnItem;
    void Settings(object sender,RoutedEventArgs e)
    {
        searchDelay.Stop();
        if(section!="Настройки"){settingsReturnSection=section;settingsReturnItem=current;}
        section="Настройки";current=null;Render();
    }
    void RenderSettings()
    {
        var back=ActionButton("Вернуться","IconBack",()=>{section=settingsReturnSection;current=settingsReturnItem;Render();});
        back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new(0,0,0,12);PageHeader.Children.Add(back);
        var title=Text("Настройки",30);title.FontWeight=FontWeights.SemiBold;PageHeader.Children.Add(title);
        var subtitle=Text("Всё для комфортного просмотра и загрузок. Изменения сохраняются сразу.",13,true);subtitle.Margin=new(0,0,0,22);PageHeader.Children.Add(subtitle);
        var content=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,10,12)};
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        StackPanel Card(string heading,string description)
        {
            var panel=new StackPanel();
            var frame=new Border{Child=panel,CornerRadius=new(18),Padding=new(22),BorderThickness=new(1),Margin=new(0,0,0,16)};
            frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"Edge");content.Children.Add(frame);
            var h=Text(heading,18);h.FontWeight=FontWeights.SemiBold;panel.Children.Add(h);
            var d=Text(description,12,true);d.Margin=new(0,0,0,18);panel.Children.Add(d);return panel;
        }
        var appearance=Card("Оформление","Тёмная тема по умолчанию. Светлую можно включить вручную.");
        var themes=new WrapPanel();appearance.Children.Add(themes);
        foreach(var light in new[]{false,true})
        {
            var selected=prefs.Light==light;
            var theme=Button((selected?"✓  ":"")+(light?"Светлая":"Тёмная"),()=>
            {
                var previous=prefs.Light;prefs.Light=light;
                try{prefs.Save();}catch(Exception error){prefs.Light=previous;Status.Text="Не удалось сохранить тему: "+error.Message;return;}
                ApplyTheme();Render();
                FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)==(light?"Светлая тема":"Тёмная тема"))?.Focus();
            });
            theme.MinWidth=136;theme.MinHeight=44;theme.Margin=new(0,0,10,8);
            theme.SetResourceReference(Control.BackgroundProperty,selected?"Selected":"Panel");
            theme.SetResourceReference(Control.BorderBrushProperty,selected?"Accent":"Edge");
            AutomationProperties.SetName(theme,light?"Светлая тема":"Тёмная тема");themes.Children.Add(theme);
        }
        var files=Card("Папка загрузок","Новые загрузки сохраняются сюда. У уже добавленных задач остаётся прежняя папка.");
        var folder=Text(prefs.Folder,14);folder.Name="SettingsFolder";folder.Margin=new(0,0,0,14);files.Children.Add(folder);
        var folderActions=new WrapPanel();files.Children.Add(folderActions);
        folderActions.Children.Add(ActionButton("Изменить папку","IconSettings",()=>
        {
            var picker=new OpenFolderDialog{Title="Папка для новых загрузок",InitialDirectory=Directory.Exists(prefs.Folder)?prefs.Folder:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
            if(picker.ShowDialog(this)!=true)return;
            try{SetDownloadFolder(picker.FolderName);folder.Text=prefs.Folder;Status.Text="Папка загрузок сохранена.";}
            catch(Exception error){Status.Text="Не удалось сохранить папку: "+error.Message;}
        }));
        folderActions.Children.Add(Button("Открыть папку",()=>
        {
            try{if(!Directory.Exists(prefs.Folder))throw new DirectoryNotFoundException("Папка недоступна. Выбери другую папку для загрузок.");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(prefs.Folder){UseShellExecute=true});}
            catch(Exception error){Status.Text=error.Message;}
        }));
        var startup=Card("При запуске","Возвращайся к загрузкам с того места, где остановился.");
        var resumeLabel=Text("Продолжать активные загрузки при запуске",13);resumeLabel.Margin=new(0);
        var resume=new CheckBox{Name="SettingsAutoResume",Style=(Style)FindResource("SettingsSwitch"),Content=resumeLabel,IsChecked=prefs.AutoResumeDownloads,VerticalContentAlignment=VerticalAlignment.Center,Margin=new(0,0,0,8)};
        resume.SetResourceReference(Control.ForegroundProperty,"Text");AutomationProperties.SetName(resume,"Продолжать активные загрузки при запуске");
        void SaveResume()
        {
            var previous=prefs.AutoResumeDownloads;prefs.AutoResumeDownloads=resume.IsChecked==true;
            try{prefs.Save();}
            catch(Exception error){prefs.AutoResumeDownloads=previous;resume.IsChecked=previous;Status.Text="Не удалось сохранить настройки: "+error.Message;}
        }
        resume.Click+=(_,_)=>SaveResume();startup.Children.Add(resume);startup.Children.Add(Text("Загрузки, которые ты поставил на паузу вручную, останутся на паузе.",12,true));
        RenderUpdateSettings(Card("Обновления","Новые версии из официального репозитория GitHub."));
        var about=Card("Качалка","Нативное приложение для Windows. Версия "+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??""));
        about.Children.Add(Text("Постеры загружаются по мере просмотра. Торрент-движок работает, пока есть активные задачи, и освобождает ресурсы после их остановки.",12,true));
    }
}
