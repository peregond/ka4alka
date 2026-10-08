using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        back.Content=IconLabel("","IconBack");back.ToolTip="Вернуться";back.Style=(Style)FindResource("QuietButton");back.Width=36;back.Height=36;back.MinHeight=36;back.Padding=new(9);back.Margin=new(0,0,10,0);
        var header=new Grid{Margin=new(0,0,0,compactHeight?8:12)};
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        header.Children.Add(back);
        var title=Text("Настройки",compactHeight?23:28);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);header.Children.Add(title);PageHeader.Children.Add(header);
        var subtitle=Text("Изменения сохраняются автоматически.",12,true);subtitle.Margin=new(46,0,0,16);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        var content=new StackPanel{MaxWidth=800,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(0,0,6,12)};
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        StackPanel Card(string heading,string description,string icon)
        {
            var panel=new StackPanel();
            var frame=new Border{Child=panel,CornerRadius=new(14),Padding=new(18),BorderThickness=new(1),Margin=new(0,0,0,12)};
            frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");content.Children.Add(frame);
            var headingRow=new Grid{Margin=new(0,0,0,14)};
            headingRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});headingRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var glyph=new System.Windows.Shapes.Path{Data=(Geometry)FindResource(icon),Width=17,Height=17,Stretch=Stretch.Uniform,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};glyph.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty,"Accent");
            var iconFrame=new Border{Child=glyph,Width=34,Height=34,CornerRadius=new(10),Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Top};iconFrame.SetResourceReference(Border.BackgroundProperty,"AccentSoft");headingRow.Children.Add(iconFrame);
            var headings=new StackPanel();Grid.SetColumn(headings,1);headingRow.Children.Add(headings);
            var h=Text(heading,16);h.FontWeight=FontWeights.SemiBold;h.Margin=new(0,0,0,3);headings.Children.Add(h);
            var d=Text(description,12,true);d.Margin=new(0);headings.Children.Add(d);panel.Children.Add(headingRow);return panel;
        }
        void Divider(StackPanel panel)
        {
            var line=new Border{Height=1,Margin=new(0,16,0,14)};line.SetResourceReference(Border.BackgroundProperty,"EdgeSoft");panel.Children.Add(line);
        }
        TextBlock Notice(StackPanel panel)
        {
            var notice=Text("",12);notice.Margin=new(0,4,0,0);notice.Visibility=Visibility.Collapsed;panel.Children.Add(notice);return notice;
        }
        void Feedback(TextBlock notice,string message,bool error=false)
        {
            notice.Text=message;notice.SetResourceReference(TextBlock.ForegroundProperty,error?"Danger":"Accent");notice.Visibility=Visibility.Visible;Status.Text=message;
        }
        var appearance=Card("Оформление","Выбери удобную тему для приложения.","IconTheme");
        var themes=new WrapPanel{Margin=new(0,0,-8,-4)};appearance.Children.Add(themes);
        foreach(var light in new[]{false,true})
        {
            var selected=prefs.Light==light;
            var theme=Button(light?"Светлая":"Тёмная",()=>
            {
                var previous=prefs.Light;prefs.Light=light;
                try{prefs.Save();}catch(Exception error){prefs.Light=previous;Status.Text="Не удалось сохранить тему: "+error.Message;return;}
                ApplyTheme();Render();
                FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)==(light?"Светлая тема":"Тёмная тема"))?.Focus();
            });
            theme.Width=138;theme.Padding=new(8);theme.Margin=new(0,0,8,4);theme.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            theme.SetResourceReference(Control.BackgroundProperty,selected?"Selected":"PanelAlt");
            theme.SetResourceReference(Control.BorderBrushProperty,selected?"Accent":"EdgeSoft");
            var themeContent=new StackPanel();
            SolidColorBrush Swatch(string value)=>new((Color)ColorConverter.ConvertFromString(value));
            var preview=new Grid{Height=50};preview.ColumnDefinitions.Add(new(){Width=new GridLength(25)});preview.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var sidebar=new Border{Background=Swatch(light?"#E7ECF8":"#0C1426"),CornerRadius=new(5,0,0,5)};preview.Children.Add(sidebar);
            var selectedRow=new Border{Background=Swatch(light?"#4937CF":"#977FFF"),Height=4,Margin=new(5,12,5,0),VerticalAlignment=VerticalAlignment.Top,CornerRadius=new(2)};sidebar.Child=selectedRow;
            var previewBody=new Border{Background=Swatch(light?"#F2F5FF":"#111B30"),CornerRadius=new(0,5,5,0),Padding=new(6)};Grid.SetColumn(previewBody,1);preview.Children.Add(previewBody);
            var previewRows=new StackPanel();previewBody.Child=previewRows;
            previewRows.Children.Add(new Border{Background=Swatch(light?"#DDE4E8":"#324468"),Height=5,Margin=new(0,0,18,6),CornerRadius=new(2)});
            var posters=new UniformGrid{Columns=3,Height=26};previewRows.Children.Add(posters);
            foreach(var color in new[]{light?"#C8D6DB":"#526873",light?"#CDC8FA":"#5C4E97",light?"#D7CFBF":"#776A54"})posters.Children.Add(new Border{Background=Swatch(color),CornerRadius=new(3),Margin=new(0,0,3,0)});
            themeContent.Children.Add(preview);
            var themeLabel=new Grid{Margin=new(2,9,2,1)};themeLabel.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});themeLabel.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var label=Text(light?"Светлая":"Тёмная",12);label.Margin=new(0);label.FontWeight=selected?FontWeights.SemiBold:FontWeights.Medium;themeLabel.Children.Add(label);
            var check=Text(selected?"✓":"",12);check.Margin=new(6,0,0,0);check.SetResourceReference(TextBlock.ForegroundProperty,"Accent");Grid.SetColumn(check,1);themeLabel.Children.Add(check);themeContent.Children.Add(themeLabel);theme.Content=themeContent;
            AutomationProperties.SetName(theme,light?"Светлая тема":"Тёмная тема");themes.Children.Add(theme);
        }
        var catalogue=Card("Каталог","Выбери страну для отечественных подборок фильмов и сериалов.","IconMovies");
        var countryLabel=Text("Отечественное кино",12,true);countryLabel.Margin=new(0,0,0,7);catalogue.Children.Add(countryLabel);
        var homeCountries=CatalogChoices.Countries.OrderBy(choice=>choice.Label).ToArray();
        var homeCountry=new ComboBox{Name="SettingsHomeCountry",ItemsSource=homeCountries,SelectedItem=homeCountries.First(choice=>choice.Key==CatalogRegions.HomeCountry(prefs.HomeCountry)),MaxWidth=320,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220};
        AutomationProperties.SetName(homeCountry,"Страна отечественных подборок");catalogue.Children.Add(homeCountry);
        var countryHint=Text("Учитываем страну производства. Фильмы совместного производства с выбранной страной тоже входят в отечественные подборки.",12,true);countryHint.Margin=new(0,8,0,0);catalogue.Children.Add(countryHint);
        var countryNotice=Notice(catalogue);
        homeCountry.SelectionChanged+=(_,_)=>
        {
            if(homeCountry.SelectedItem is not CatalogChoice choice)return;
            var previous=prefs.HomeCountry;prefs.HomeCountry=choice.Key;
            try{prefs.Save();ResetDiscoveryData();catalogPages.Clear();liveKey="";Feedback(countryNotice,"Страна подборок сохранена.");}
            catch(Exception error){prefs.HomeCountry=previous;Feedback(countryNotice,"Не удалось сохранить страну: "+error.Message,true);}
        };
        var files=Card("Загрузки","Папка для новых файлов и поведение при запуске.","IconDownload");
        var folderLabel=Text("Сохранять файлы в",12,true);folderLabel.Margin=new(0,0,0,7);files.Children.Add(folderLabel);
        var folder=Text(prefs.Folder,13);folder.Name="SettingsFolder";folder.Margin=new(0);folder.ToolTip=prefs.Folder;
        var folderFrame=new Border{Child=folder,CornerRadius=new(9),Padding=new(12,10,12,10),Margin=new(0,0,0,10),BorderThickness=new(1)};folderFrame.SetResourceReference(Border.BackgroundProperty,"PanelAlt");folderFrame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");files.Children.Add(folderFrame);
        files.Children.Add(Text("В выбранной папке создаётся Ka4alka. Уже добавленные загрузки сохраняют прежнюю папку.",12,true));
        var folderActions=new WrapPanel();files.Children.Add(folderActions);
        var folderNotice=Notice(files);
        folderActions.Children.Add(ActionButton("Изменить папку","IconSettings",()=>
        {
            var picker=new OpenFolderDialog{Title="Выберите папку — внутри будет создана Ka4alka для загрузок",InitialDirectory=Directory.Exists(prefs.Folder)?prefs.Folder:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
            if(picker.ShowDialog(this)!=true)return;
            try{SetDownloadFolder(picker.FolderName);folder.Text=prefs.Folder;folder.ToolTip=prefs.Folder;Feedback(folderNotice,"Папка загрузок сохранена.");}
            catch(Exception error){Feedback(folderNotice,"Не удалось сохранить папку: "+error.Message,true);}
        }));
        folderActions.Children.Add(ActionButton("Открыть папку","IconFolder",()=>
        {
            try{if(!Directory.Exists(prefs.Folder))throw new DirectoryNotFoundException("Папка недоступна. Выбери другую папку для загрузок.");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(prefs.Folder){UseShellExecute=true});}
            catch(Exception error){Feedback(folderNotice,error.Message,true);}
        }));
        Divider(files);
        var resumeLabel=Text("Продолжать активные загрузки при запуске",13);resumeLabel.Margin=new(0);
        var resume=new CheckBox{Name="SettingsAutoResume",Style=(Style)FindResource("SettingsSwitch"),Content=resumeLabel,IsChecked=prefs.AutoResumeDownloads,VerticalContentAlignment=VerticalAlignment.Center,Margin=new(0,0,0,8)};
        resume.SetResourceReference(Control.ForegroundProperty,"Text");AutomationProperties.SetName(resume,"Продолжать активные загрузки при запуске");
        files.Children.Add(resume);files.Children.Add(Text("Загрузки, поставленные на паузу вручную, останутся на паузе.",12,true));
        var resumeNotice=Notice(files);
        void SaveResume()
        {
            var previous=prefs.AutoResumeDownloads;prefs.AutoResumeDownloads=resume.IsChecked==true;
            try{prefs.Save();Feedback(resumeNotice,"Настройка сохранена.");}
            catch(Exception error){prefs.AutoResumeDownloads=previous;resume.IsChecked=previous;Feedback(resumeNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        }
        resume.Click+=(_,_)=>SaveResume();
        var system=Card("Windows","Автозапуск и подключения участников раздачи.","IconSettings");
        var startupNotice=Notice(system);
        var startup=new CheckBox{Name="SettingsStartup",Content=Text("Открывать Качалку при входе в Windows"),IsChecked=WindowsIntegration.StartupEnabled,Style=(Style)FindResource("SettingsSwitch"),Margin=new(0,0,0,12)};
        AutomationProperties.SetName(startup,"Запускать Качалку вместе с Windows");system.Children.Add(startup);
        startup.Click+=(_,_)=>
        {
            try{WindowsIntegration.SetStartup(startup.IsChecked==true);Feedback(startupNotice,startup.IsChecked==true?"Качалка будет открываться при входе в Windows.":"Автозапуск выключен.");}
            catch(Exception error){startup.IsChecked=WindowsIntegration.StartupEnabled;Feedback(startupNotice,"Не удалось изменить автозапуск: "+error.Message,true);}
        };
        Divider(system);
        system.Children.Add(Text("Брандмауэр",13));
        system.Children.Add(Text("Разреши входящие TCP и UDP для Качалки в частных сетях. Windows попросит права администратора. Это может помочь поиску участников раздачи; в общедоступных сетях исключение не применяется.",12,true));
        var firewallNotice=Notice(system);
        var firewall=AsyncButton("Разрешить в брандмауэре",async()=>
        {
            try{Feedback(firewallNotice,await WindowsIntegration.AllowFirewallAsync()?"Исключение для Качалки добавлено.":"Запрос отменён. Настройки брандмауэра не изменены.");}
            catch(Exception error){ErrorLog.Write(error);Feedback(firewallNotice,error.Message,true);}
        });
        AutomationProperties.SetName(firewall,"Разрешить Качалку в брандмауэре");firewall.HorizontalAlignment=HorizontalAlignment.Left;system.Children.Add(firewall);
        RenderUpdateSettings(Card("Обновления","Приложение всегда под рукой в актуальной версии.","IconRefresh"));
        var cache=Card("Кэш","Постеры, фотографии и данные каталога: до 1 ГБ. Раз в неделю удаляем данные, не использованные за последние семь дней. При заполнении — самые давно не открывавшиеся.","IconRefresh");
        var cacheSize=Text("Подсчитываем размер…",13);cacheSize.Name="SettingsCacheSize";cache.Children.Add(cacheSize);
        var cacheNotice=Notice(cache);
        var clearCache=AsyncButton("Очистить кэш",async()=>
        {
            var result=await ClearCacheAsync();
            cacheSize.Text="Занято "+CacheSize(result.Bytes)+" из 1 ГБ";
            Feedback(cacheNotice,"Освобождено "+CacheSize(result.FreedBytes)+(result.Failed>0?". Некоторые файлы заняты — очистка повторится автоматически.":"."));
        });
        clearCache.Name="ClearCacheButton";AutomationProperties.SetName(clearCache,"Очистить кэш");clearCache.HorizontalAlignment=HorizontalAlignment.Left;cache.Children.Add(clearCache);
        cache.Children.Add(Text("Скачанные файлы, очередь загрузок, избранное и настройки сохраняются.",12,true));
        _=ShowCacheSizeAsync(cacheSize);
        var diagnostics=Card("Помощь и диагностика","Отчёт поможет разобраться с ошибкой. Пути к личным папкам и ключи доступа скрываются.","IconInfo");
        var diagnosticActions=new WrapPanel();diagnostics.Children.Add(diagnosticActions);
        var diagnosticNotice=Notice(diagnostics);
        diagnosticActions.Children.Add(ActionButton("Скопировать логи","IconFile",()=>
        {
            try{Clipboard.SetText(DiagnosticReport.Create(prefs,downloads));Feedback(diagnosticNotice,"Диагностический отчёт скопирован.");}
            catch(Exception error){Feedback(diagnosticNotice,"Не удалось скопировать логи: "+error.Message,true);}
        }));
        diagnosticActions.Children.Add(ActionButton("Сообщить об ошибке","IconInfo",()=>
        {
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiagnosticReport.IssueUrl){UseShellExecute=true});}
            catch(Exception error){Feedback(diagnosticNotice,"Не удалось открыть форму: "+error.Message,true);}
        }));
        diagnostics.Children.Add(Text("Отчёт отправляется только вручную — приложение само его никуда не отправляет.",11,true));
        var about=new WrapPanel{Margin=new(4,4,0,0)};content.Children.Add(about);
        var appName=Text("Качалка",12);appName.FontWeight=FontWeights.SemiBold;appName.Margin=new(0,0,10,4);about.Children.Add(appName);
        var version=Text("Версия "+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??""),12,true);version.Margin=new(0,0,0,4);about.Children.Add(version);
    }
}
