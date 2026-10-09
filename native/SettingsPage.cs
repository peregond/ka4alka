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
    sealed record SettingsPersonNavigation(CinemaPerson? Person,MediaItem? Origin,string? Section,
        (CinemaPerson Person,MediaItem? Origin)? ReturnPerson,PersonSearchReturn? Search,SavedReturn? Saved);
    SettingsPersonNavigation? settingsPersonNavigation;
    void Settings(object sender,RoutedEventArgs e)
    {
        searchDelay.Stop();
        if(section!="Настройки")
        {
            settingsReturnSection=section;settingsReturnItem=current;
            settingsPersonNavigation=new(activePerson,personOrigin,personSection,returnPerson,personSearchReturn,savedReturn);
        }
        section="Настройки";current=null;Render();
    }
    void ReturnFromSettings()
    {
        section=settingsReturnSection;current=settingsReturnItem;
        if(settingsPersonNavigation is {} previous)
        {
            activePerson=previous.Person;personOrigin=previous.Origin;personSection=previous.Section;
            returnPerson=previous.ReturnPerson;personSearchReturn=previous.Search;savedReturn=previous.Saved;
        }
        settingsPersonNavigation=null;Render();
    }
    void RenderSettings()
    {
        SetBack("Вернуться",ReturnFromSettings);
        var header=new Grid{Margin=new(0,0,0,compactHeight?4:6)};
        var title=Text("Настройки",compactHeight?23:28);title.FontFamily=(FontFamily)FindResource("DisplayFont");title.FontWeight=FontWeights.Bold;title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);PageHeader.Children.Add(header);
        var subtitle=Text("Изменения сохраняются автоматически.",13,true);subtitle.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");subtitle.Margin=new(0,0,0,20);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
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
            var palette=InterfacePalette.For(light);
            var preview=new Grid{Height=50};preview.ColumnDefinitions.Add(new(){Width=new GridLength(25)});preview.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var sidebar=new Border{Background=palette["Sidebar"],CornerRadius=new(5,0,0,5)};preview.Children.Add(sidebar);
            var selectedRow=new Border{Background=palette["Selected"],Height=10,Margin=new(3,9,3,0),VerticalAlignment=VerticalAlignment.Top,CornerRadius=new(2),Child=new Border{Background=palette["Accent"],Height=3,Margin=new(3,0,3,0),VerticalAlignment=VerticalAlignment.Center,CornerRadius=new(1)}};sidebar.Child=selectedRow;
            var previewBody=new Border{Background=palette["Bg"],CornerRadius=new(0,5,5,0),Padding=new(6)};Grid.SetColumn(previewBody,1);preview.Children.Add(previewBody);
            var previewRows=new StackPanel();previewBody.Child=previewRows;
            previewRows.Children.Add(new Border{Background=palette["PanelAlt"],BorderBrush=palette["EdgeSoft"],BorderThickness=new(1),Height=5,Margin=new(0,0,18,6),CornerRadius=new(2)});
            var posters=new UniformGrid{Columns=3,Height=26};previewRows.Children.Add(posters);
            foreach(var key in new[]{"Primary","Muted","RatingInk"})posters.Children.Add(new Border{Background=palette[key],CornerRadius=new(3),Margin=new(0,0,3,0)});
            themeContent.Children.Add(preview);
            var themeLabel=new Grid{Margin=new(2,9,2,1)};themeLabel.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});themeLabel.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var label=Text(light?"Светлая":"Тёмная",12);label.Margin=new(0);label.FontWeight=selected?FontWeights.SemiBold:FontWeights.Medium;themeLabel.Children.Add(label);
            var check=Text(selected?"✓":"",12);check.Margin=new(6,0,0,0);check.SetResourceReference(TextBlock.ForegroundProperty,"Accent");Grid.SetColumn(check,1);themeLabel.Children.Add(check);themeContent.Children.Add(themeLabel);theme.Content=themeContent;
            AutomationProperties.SetName(theme,light?"Светлая тема":"Тёмная тема");themes.Children.Add(theme);
        }
        var catalogue=Card("Каталог","Выбери страну для отечественных подборок фильмов и сериалов.","IconMovies");
        var countryLabel=Text("Отечественное кино",12,true);countryLabel.Margin=new(0,0,0,7);catalogue.Children.Add(countryLabel);
        var homeCountries=CatalogChoices.Countries.OrderBy(choice=>choice.Label).ToArray();
        var homeCountry=new ComboBox{Name="SettingsHomeCountry",ItemsSource=homeCountries,SelectedItem=homeCountries.First(choice=>choice.Key==CatalogRegions.HomeCountry(prefs.HomeCountry)),MaxWidth=320,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=0,Margin=new(0,4,0,10)};
        homeCountry.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)){Source=catalogue});
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
        Divider(files);
        var recovery=new CheckBox{Name="SettingsAutoRecover",Style=(Style)FindResource("SettingsSwitch"),Content=Text("Восстанавливать подключения после обрыва сети"),IsChecked=prefs.AutoRecoverDownloads,Margin=new(0,0,0,8)};
        AutomationProperties.SetName(recovery,"Восстанавливать загрузки после обрыва сети");files.Children.Add(recovery);
        files.Children.Add(Text("Активные загрузки продолжатся при возвращении сети и после сна. Задачи на ручной паузе останутся на паузе.",12,true));
        var recoveryNotice=Notice(files);
        recovery.Click+=(_,_)=>
        {
            var previous=prefs.AutoRecoverDownloads;prefs.AutoRecoverDownloads=recovery.IsChecked==true;
            try{prefs.Save();Feedback(recoveryNotice,"Настройка сохранена.");if(prefs.AutoRecoverDownloads)RequestDownloadRecovery(true);}
            catch(Exception error){prefs.AutoRecoverDownloads=previous;recovery.IsChecked=previous;Feedback(recoveryNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        };
        Divider(files);
        files.Children.Add(Text("Свободное место",13));
        files.Children.Add(Text("Перед началом и во время скачивания проверяем место для оставшихся файлов и других активных загрузок на том же диске. Оставляем запас 256 МБ. Если места не хватает, загрузка встанет на паузу: освободи место и нажми «Продолжить».",12,true));
        var notifications=Card("Уведомления","Windows сообщит о завершении загрузки, ошибке или нехватке места.","IconInfo");
        var notify=new CheckBox{Name="SettingsNotifyDownloads",Style=(Style)FindResource("SettingsSwitch"),Content=Text("Уведомления о загрузках"),IsChecked=prefs.NotifyDownloads,Margin=new(0,0,0,8)};
        AutomationProperties.SetName(notify,"Уведомления Windows о загрузках");notifications.Children.Add(notify);
        notifications.Children.Add(Text("Повторяющиеся ошибки не создают поток уведомлений. Нажатие на уведомление откроет очередь. Windows может скрывать уведомления в режиме «Не беспокоить».",12,true));
        var notificationNotice=Notice(notifications);
        notify.Click+=(_,_)=>
        {
            var previous=prefs.NotifyDownloads;prefs.NotifyDownloads=notify.IsChecked==true;
            try{prefs.Save();if(!prefs.NotifyDownloads)downloadNotifications?.Dismiss();Feedback(notificationNotice,"Настройка сохранена.");}
            catch(Exception error){prefs.NotifyDownloads=previous;notify.IsChecked=previous;Feedback(notificationNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        };
        var testNotification=ActionButton("Проверить уведомление","IconInfo",()=>Feedback(notificationNotice,ShowTestDownloadNotification()?"Уведомление передано Windows. Если оно скрыто, проверь настройки уведомлений и режим «Не беспокоить».":prefs.NotifyDownloads?"Windows не приняла уведомление. Попробуй ещё раз.":"Включи уведомления о загрузках для проверки.",!prefs.NotifyDownloads));
        AutomationProperties.SetName(testNotification,"Проверить уведомление Windows");notifications.Children.Add(testNotification);
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
        RenderLanSettings(Card("Устройства в сети","Отправляй загрузки на другой компьютер с Качалкой.","IconSeries"));
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
        var copyingReport=false;
        diagnosticActions.Children.Add(ActionButton("Скопировать логи","IconFile",async()=>
        {
            if(copyingReport)return;copyingReport=true;
            try{var report=await DiagnosticReport.CreateAsync(prefs,downloads,reliabilityCancellation.Token);if(closing||closed)return;Clipboard.SetText(report);Feedback(diagnosticNotice,"Диагностический отчёт скопирован.");}
            catch(OperationCanceledException)when(closing||closed){}
            catch(Exception error){Feedback(diagnosticNotice,"Не удалось скопировать логи: "+error.Message,true);}
            finally{copyingReport=false;}
        }));
        diagnosticActions.Children.Add(ActionButton("Сообщить об ошибке","IconInfo",()=>
        {
            try{ShowBugReport();}
            catch(Exception error){Feedback(diagnosticNotice,"Не удалось открыть форму: "+error.Message,true);}
        }));
        diagnostics.Children.Add(Text("Отчёт отправляется только вручную — приложение само его никуда не отправляет.",11,true));
        var about=new WrapPanel{Margin=new(4,4,0,0)};content.Children.Add(about);
        var appName=Text("Качалка",12);appName.FontWeight=FontWeights.SemiBold;appName.Margin=new(0,0,10,4);about.Children.Add(appName);
        var version=Text("Версия "+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??""),12,true);version.Margin=new(0,0,0,4);about.Children.Add(version);
    }
}
