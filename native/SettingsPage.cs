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
    void ChangeInterfaceLanguage(string language)
    {
        var previous=prefs.UiLanguage;prefs.UiLanguage=AppLanguage.Normalize(language);
        try{prefs.Save();}catch(Exception error){prefs.UiLanguage=previous;Status.Text=AppLanguage.T("Не удалось сохранить язык: "+error.Message);Render();return;}
        CancelDetailMetadata(true);cardMetadata.Clear();priorityMetadata.Clear();
        foreach(var lookup in ratingLookups.Values)lookup.Request.Cancel();ratingLookups.Clear();
        CancelGameWork();gameHomeRequest?.Cancel();gameBannerRequest?.Cancel();gameDetailRequest?.Cancel();
        gameCatalogRequest?.Cancel();gameCatalogRequest=null;gameLoading=false;gameHomeRequest=null;gameBannerRequest=null;
        gamePickerStates.Clear();gameDetails.Clear();gameItems=[];gameShelfItems.Clear();gameShelfLoading.Clear();gameShelfFailed.Clear();gameShelfLoadedUtc=default;gameDetailId=null;gameKey="";
        AppLanguage.Set(prefs.UiLanguage);Status.Text="";
        foreach(var item in downloads.Items)item.Refresh();
        layoutInitialized=false;ApplyCompactLayout();Render();UpdateLoadingStateAfterLanguageChange();
        foreach(var button in Navigation.Children.OfType<Button>())if(button.Tag is string name){AutomationProperties.SetName(button,AppLanguage.T(name));button.ToolTip=AppLanguage.T(name);}
    }
    void UpdateLoadingStateAfterLanguageChange(){RefreshLoadingIndicator();RefreshSidebarUpdate();UpdateDownloadsWidget();}
    void RenderSettings()
    {
        SetBack("Вернуться",ReturnFromSettings);
        var header=new Grid{Margin=new(0,0,0,compactHeight?4:6)};
        var title=Text("Настройки",compactHeight?23:28);title.FontFamily=(FontFamily)FindResource("DisplayFont");title.FontWeight=FontWeights.Bold;title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);PageHeader.Children.Add(header);
        var subtitle=Text("Всё сохраняется сразу.",13,true);subtitle.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");subtitle.Margin=new(0,0,0,12);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        // Two columns on wide windows: section navigation on the left, cards on the right (up to 820 wide).
        var content=new StackPanel{MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,6,12)};
        var settingsScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var settingsLayout=new Grid{Name="SettingsLayout"};settingsLayout.ColumnDefinitions.Add(new(){Width=new GridLength(0)});settingsLayout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var sectionNav=new StackPanel{Name="SettingsSections",Margin=new(0,0,24,0),Visibility=Visibility.Collapsed};settingsLayout.Children.Add(sectionNav);
        Grid.SetColumn(settingsScroll,1);settingsLayout.Children.Add(settingsScroll);Body.Children.Add(settingsLayout);
        var sectionFrames=new Dictionary<string,Border>();var sectionIcons=new Dictionary<string,string>();
        StackPanel Card(string heading,string description,string icon)
        {
            var panel=new StackPanel();
            var frame=new Border{Child=panel,CornerRadius=new(16),Padding=new(18),BorderThickness=new(1),Margin=new(0,0,0,14),Tag=heading};
            frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");content.Children.Add(frame);sectionFrames[heading]=frame;sectionIcons[heading]=icon;
            // Each section carries its own icon on a soft tile, the same one as in the section list.
            var headings=new DockPanel{Margin=new(0,0,0,14),LastChildFill=true};
            var tile=new Border{Width=32,Height=32,CornerRadius=new(9),Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Top};tile.SetResourceReference(Border.BackgroundProperty,"AccentSoft");
            var glyph=new System.Windows.Shapes.Path{Data=(Geometry)FindResource(icon),Width=18,Height=18,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Accent");tile.Child=glyph;DockPanel.SetDock(tile,Dock.Left);headings.Children.Add(tile);
            var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};headings.Children.Add(texts);
            var h=Text(heading,17);h.FontWeight=FontWeights.Bold;h.Margin=new(0,0,0,3);texts.Children.Add(h);
            if(description.Length>0)texts.Children.Add(Hint(description));
            panel.Children.Add(headings);return panel;
        }
        void Divider(StackPanel panel)
        {
            var line=new Border{Height=1,Margin=new(0,12,0,12)};line.SetResourceReference(Border.BackgroundProperty,"EdgeSoft");panel.Children.Add(line);
        }
        TextBlock Notice(StackPanel panel)
        {
            var notice=Text("",12);notice.Margin=new(0,4,0,0);notice.Visibility=Visibility.Collapsed;panel.Children.Add(notice);return notice;
        }
        void Feedback(TextBlock notice,string message,bool error=false)
        {
            notice.Text=AppLanguage.T(message);notice.SetResourceReference(TextBlock.ForegroundProperty,error?"Danger":"Accent");notice.Visibility=Visibility.Visible;Status.Text=AppLanguage.T(message);
        }
        var appearance=Card("Внешний вид","Язык, тема и режим для слабых компьютеров.","IconTheme");
        var languageLabel=Text("Язык интерфейса",13);languageLabel.FontWeight=FontWeights.SemiBold;appearance.Children.Add(languageLabel);
        var language=new ComboBox{Name="SettingsLanguage",ItemsSource=new[]{"Русский","English"},SelectedIndex=prefs.UiLanguage=="en"?1:0,MaxWidth=320,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,12)};
        AutomationProperties.SetName(language,AppLanguage.T("Язык интерфейса"));appearance.Children.Add(language);
        language.SelectionChanged+=(_,_)=>{if(language.SelectedIndex<0)return;ChangeInterfaceLanguage(language.SelectedIndex==1?"en":"ru");};
        var themeCaption=Text("Тема",13);themeCaption.FontWeight=FontWeights.SemiBold;themeCaption.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");themeCaption.Margin=new(0,0,0,10);appearance.Children.Add(themeCaption);
        var themes=new UniformGrid{Columns=2,Margin=new(0,0,-12,-12)};appearance.Children.Add(themes);
        themes.SizeChanged+=(_,_)=>{var columns=ScrollStableWidth(themes)>=440?2:1;if(themes.Columns!=columns)themes.Columns=columns;};
        foreach(var light in new[]{false,true})
        {
            var selected=prefs.Light==light;
            var theme=Button("",()=>
            {
                var previous=prefs.Light;prefs.Light=light;
                try{prefs.Save();}catch(Exception error){prefs.Light=previous;Status.Text=AppLanguage.T("Не удалось сохранить тему: "+error.Message);return;}
                ApplyTheme();Render();
                FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)==AppLanguage.T(light?"Светлая тема":"Тёмная тема"))?.Focus();
            });
            theme.Style=(Style)FindResource("ChoiceCard");theme.Padding=new(12);theme.Margin=new(0,0,12,12);theme.BorderThickness=new(2);theme.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            theme.SetResourceReference(Control.BackgroundProperty,"PanelAlt");
            theme.SetResourceReference(Control.BorderBrushProperty,selected?"Accent":"EdgeSoft");
            var themeContent=new StackPanel();
            var palette=InterfacePalette.For(light);
            // Miniature of the real interface drawn from the frozen palette of that theme.
            var preview=new Grid{Height=72};preview.ColumnDefinitions.Add(new(){Width=new GridLength(40)});preview.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var sidebar=new Border{Background=palette["Sidebar"],CornerRadius=new(8,0,0,8),Padding=new(6,10,6,0)};preview.Children.Add(sidebar);
            var sideRows=new StackPanel();sidebar.Child=sideRows;
            sideRows.Children.Add(new Border{Background=palette["Accent"],Height=5,Margin=new(0,0,10,8),CornerRadius=new(2)});
            sideRows.Children.Add(new Border{Background=palette["NavSelected"],Height=9,Margin=new(0,0,0,4),CornerRadius=new(3)});
            sideRows.Children.Add(new Border{Background=palette["EdgeSoft"],Height=3,Margin=new(2,3,8,0),CornerRadius=new(1)});
            var previewBody=new Border{Background=palette["Bg"],CornerRadius=new(0,8,8,0),Padding=new(8)};Grid.SetColumn(previewBody,1);preview.Children.Add(previewBody);
            var previewRows=new StackPanel();previewBody.Child=previewRows;
            previewRows.Children.Add(new Border{Background=palette["Panel"],Height=24,Margin=new(0,0,0,6),CornerRadius=new(5)});
            var posters=new UniformGrid{Columns=4,Height=34};previewRows.Children.Add(posters);
            for(var index=0;index<4;index++)posters.Children.Add(new Border{Background=palette["Raised"],CornerRadius=new(3),Margin=new(0,0,index==3?0:4,0)});
            themeContent.Children.Add(preview);
            var themeLabel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(2,12,2,2)};
            var radio=new Grid{Width=18,Height=18,Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            var ring=new System.Windows.Shapes.Ellipse{StrokeThickness=2,Width=18,Height=18};ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,selected?"Accent":"EdgeFocus");radio.Children.Add(ring);
            if(selected){var dot=new System.Windows.Shapes.Ellipse{Width=8,Height=8};dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Accent");radio.Children.Add(dot);}
            themeLabel.Children.Add(radio);
            var label=Text(light?"Светлая":"Тёмная",14);label.Margin=new(0);label.FontWeight=FontWeights.SemiBold;label.VerticalAlignment=VerticalAlignment.Center;themeLabel.Children.Add(label);
            themeContent.Children.Add(themeLabel);theme.Content=themeContent;
            AutomationProperties.SetName(theme,AppLanguage.T(light?"Светлая тема":"Тёмная тема"));themes.Children.Add(theme);
        }
        Divider(appearance);
        // Lite mode drops decorative backdrops; posters, catalog and every function stay.
        var liteTitle=new WrapPanel();liteTitle.Children.Add(SwitchTitle("Лёгкий режим"));
        var litePill=PillRadius.Apply(new Border{Padding=new(9,2,9,2),Margin=new(10,2,0,2),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=AppLanguage.T("Для слабых ноутбуков"),FontSize=11,FontWeight=FontWeights.SemiBold}});litePill.SetResourceReference(Border.BackgroundProperty,"Raised");((TextBlock)litePill.Child).SetResourceReference(TextBlock.ForegroundProperty,"Muted");liteTitle.Children.Add(litePill);
        var liteContent=new StackPanel();liteContent.Children.Add(liteTitle);var liteHint=Hint("Без фоновых обложек и лишних эффектов. На слабых компьютерах включается сам; здесь можно переключить.");liteHint.Margin=new(0,4,0,0);liteContent.Children.Add(liteHint);
        var lite=new CheckBox{Name="SettingsLiteMode",Style=(Style)FindResource("SettingsSwitch"),Content=liteContent,IsChecked=prefs.LiteMode,VerticalContentAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(lite,AppLanguage.T("Лёгкий режим"));appearance.Children.Add(lite);
        var liteNotice=Notice(appearance);
        lite.Click+=(_,_)=>
        {
            var previous=prefs.LiteMode;var configured=prefs.LiteModeConfigured;prefs.LiteMode=lite.IsChecked==true;prefs.LiteModeConfigured=true;
            try{prefs.Save();ApplyLiteMode();Feedback(liteNotice,"Настройка сохранена.");}
            catch(Exception error){prefs.LiteMode=previous;prefs.LiteModeConfigured=configured;lite.IsChecked=previous;ApplyLiteMode();Feedback(liteNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        };
        var personal=Card("Игнорируемое","Скрой названия и жанры в каталоге и подборках. В поиске, «Сохранённом» и загрузках они остаются.","IconMovies");
        RenderHiddenCatalogSettings(personal);
        var catalogue=Card("Каталог","Страна для подборок «Отечественное» и источники данных.","IconMovies");
        var countryLabel=Hint("Отечественное кино");countryLabel.Margin=new(0,0,0,7);catalogue.Children.Add(countryLabel);
        var homeCountries=CatalogChoices.Countries.OrderBy(choice=>choice.Label).ToArray();
        var homeCountry=new ComboBox{Name="SettingsHomeCountry",ItemsSource=homeCountries,SelectedItem=homeCountries.First(choice=>choice.Key==CatalogRegions.HomeCountry(prefs.HomeCountry)),MaxWidth=320,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=0,Margin=new(0,4,0,10)};
        homeCountry.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)){Source=catalogue});
        AutomationProperties.SetName(homeCountry,AppLanguage.T("Страна отечественных подборок"));catalogue.Children.Add(homeCountry);
        var countryHint=Hint("Учитываем страну производства, в том числе совместного.");countryHint.Margin=new(0,8,0,0);catalogue.Children.Add(countryHint);
        var countryNotice=Notice(catalogue);
        // What the catalog holds and when it was last checked, with a way to check right now.
        var catalogStatus=Hint("");catalogStatus.Name="SettingsCatalogStatus";catalogStatus.Margin=new(0,16,0,0);catalogue.Children.Add(catalogStatus);
        void ShowCatalogStatus()
        {
            var culture=System.Globalization.CultureInfo.GetCultureInfo(AppLanguage.Current=="en"?"en-US":"ru-RU");
            string Moment(DateTime utc)=>utc==default?AppLanguage.T("ещё не проверялось"):utc.ToLocalTime().ToString("d MMMM, HH:mm",culture);
            catalogStatus.Text=string.Format(AppLanguage.T("В каталоге {0} названий. Данные от {1}. Проверено: {2}."),sharedCatalog.All.Count,Moment(sharedCatalog.GeneratedUtc),Moment(sharedCatalog.LastCheckedUtc));
        }
        ShowCatalogStatus();
        var catalogNotice=Notice(catalogue);
        var checkCatalog=ActionButton("Обновить каталог","IconRefresh",async()=>
        {
            Feedback(catalogNotice,"Проверяем каталог…");
            try
            {
                var changed=await sharedCatalog.RefreshAsync(force:true);
                if(closed)return;
                ShowCatalogStatus();
                if(changed){catalogPages.Clear();liveKey="";}
                Feedback(catalogNotice,changed?"Каталог обновлён. Новые названия появятся в разделах.":"Новых названий пока нет.");
            }
            catch(Exception error){if(!closed)Feedback(catalogNotice,"Не удалось проверить каталог: "+error.Message,true);}
        });
        checkCatalog.Margin=new(0,10,0,0);checkCatalog.HorizontalAlignment=HorizontalAlignment.Left;checkCatalog.Name="SettingsCheckCatalog";catalogue.Children.Add(checkCatalog);
        var tmdbLogo=new Image{Name="TmdbLogo",Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/tmdb-logo.png")),Width=128,Height=17,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,18,0,8)};
        AutomationProperties.SetName(tmdbLogo,AppLanguage.T("The Movie Database — источник широких фонов"));catalogue.Children.Add(tmdbLogo);
        var artworkAttribution=Hint("Широкие фоны — TMDB. This product uses the TMDB API but is not endorsed or certified by TMDB.");artworkAttribution.Name="TmdbAttribution";catalogue.Children.Add(artworkAttribution);
        // TheTVDB's licence for small projects requires this credit with a direct link to the site.
        var tvdbAttribution=Hint("Описания сериалов, которых нет в русском каталоге — ");tvdbAttribution.Name="TvdbAttribution";tvdbAttribution.Margin=new(0,8,0,0);
        var tvdbLink=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Metadata provided by TheTVDB"));tvdbLink.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Accent");
        tvdbLink.Click+=(_,_)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.thetvdb.com/"){UseShellExecute=true});}catch(Exception){}};
        tvdbAttribution.Inlines.Add(tvdbLink);tvdbAttribution.Inlines.Add(new System.Windows.Documents.Run("."));catalogue.Children.Add(tvdbAttribution);
        homeCountry.SelectionChanged+=(_,_)=>
        {
            if(homeCountry.SelectedItem is not CatalogChoice choice)return;
            var previous=prefs.HomeCountry;prefs.HomeCountry=choice.Key;
            try{prefs.Save();ResetDiscoveryData();catalogPages.Clear();liveKey="";Feedback(countryNotice,"Страна подборок сохранена.");}
            catch(Exception error){prefs.HomeCountry=previous;Feedback(countryNotice,"Не удалось сохранить страну: "+error.Message,true);}
        };
        var files=Card("Загрузки","Папка, возобновление и свободное место.","IconDownload");
        var folderLabel=Text("Папка загрузок",13);folderLabel.FontWeight=FontWeights.SemiBold;folderLabel.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");folderLabel.Margin=new(0,0,0,8);files.Children.Add(folderLabel);
        var folderRow=new Grid{Margin=new(0,0,0,10)};folderRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});folderRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});folderRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});files.Children.Add(folderRow);
        var folder=new TextBlock{Name="SettingsFolder",Text=prefs.Folder,FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,ToolTip=prefs.Folder};folder.SetResourceReference(TextBlock.ForegroundProperty,"Text");
        var folderGlyph=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconFolder"),Width=18,Height=18,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeLineJoin=PenLineJoin.Round,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};folderGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Subtle");
        var folderBox=new DockPanel();DockPanel.SetDock(folderGlyph,Dock.Left);folderBox.Children.Add(folderGlyph);folderBox.Children.Add(folder);
        var folderFrame=new Border{Child=folderBox,CornerRadius=new(12),Padding=new(14,0,14,0),Height=48,Margin=new(0,0,10,0),BorderThickness=new(1)};folderFrame.SetResourceReference(Border.BackgroundProperty,"PanelAlt");folderFrame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");folderRow.Children.Add(folderFrame);
        folderRow.SizeChanged+=(_,e)=>
        {
            if(!e.WidthChanged)return;var narrowRow=e.NewSize.Width<440;
            if(narrowRow&&folderRow.RowDefinitions.Count==0){folderRow.RowDefinitions.Add(new());folderRow.RowDefinitions.Add(new());}
            if(!narrowRow)folderRow.RowDefinitions.Clear();
            Grid.SetColumnSpan(folderFrame,narrowRow?3:1);folderFrame.Margin=narrowRow?new(0,0,0,10):new(0,0,10,0);
            foreach(var child in folderRow.Children.OfType<Button>()){Grid.SetRow(child,narrowRow?1:0);}
            if(folderRow.Children.OfType<Button>().ToArray() is [var change,var open]){Grid.SetColumn(change,narrowRow?0:1);Grid.SetColumn(open,narrowRow?1:2);}
        };
        files.Children.Add(Hint("Внутри выбранной папки Качалка создаёт Ka4alka. Уже добавленные загрузки остаются на прежнем месте."));
        var folderNotice=Notice(files);
        var changeFolder=Button("Изменить",()=>
        {
            var picker=new OpenFolderDialog{Title=AppLanguage.T("Выберите папку — внутри будет создана Ka4alka для загрузок"),InitialDirectory=Directory.Exists(prefs.Folder)?prefs.Folder:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
            if(picker.ShowDialog(this)!=true)return;
            try{SetDownloadFolder(picker.FolderName);folder.Text=prefs.Folder;folder.ToolTip=prefs.Folder;Feedback(folderNotice,"Папка загрузок сохранена.");}
            catch(Exception error){Feedback(folderNotice,"Не удалось сохранить папку: "+error.Message,true);}
        });changeFolder.Height=48;changeFolder.MinHeight=48;changeFolder.Margin=new(0,0,10,0);AutomationProperties.SetName(changeFolder,AppLanguage.T("Изменить папку"));Grid.SetColumn(changeFolder,1);folderRow.Children.Add(changeFolder);
        var openFolder=new Button{Style=(Style)FindResource("IconButton"),Width=48,Height=48,ToolTip=AppLanguage.T("Открыть в проводнике"),Content=IconLabel("","IconExternal",18)};
        openFolder.Click+=(_,_)=>
        {
            try{if(!Directory.Exists(prefs.Folder))throw new DirectoryNotFoundException("Папка недоступна. Выбери другую папку для загрузок.");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(prefs.Folder){UseShellExecute=true});}
            catch(Exception error){Feedback(folderNotice,error.Message,true);}
        };AutomationProperties.SetName(openFolder,AppLanguage.T("Открыть папку"));Grid.SetColumn(openFolder,2);folderRow.Children.Add(openFolder);
        Divider(files);
        var resumeLabel=SwitchTitle("Продолжать активные загрузки при запуске");
        var resume=new CheckBox{Name="SettingsAutoResume",Style=(Style)FindResource("SettingsSwitch"),Content=resumeLabel,IsChecked=prefs.AutoResumeDownloads,VerticalContentAlignment=VerticalAlignment.Center,Margin=new(0,0,0,8)};
        resume.SetResourceReference(Control.ForegroundProperty,"Text");AutomationProperties.SetName(resume,AppLanguage.T("Продолжать активные загрузки при запуске"));
        files.Children.Add(resume);files.Children.Add(Hint("Загрузки на ручной паузе остаются на паузе."));
        var resumeNotice=Notice(files);
        void SaveResume()
        {
            var previous=prefs.AutoResumeDownloads;prefs.AutoResumeDownloads=resume.IsChecked==true;
            try{prefs.Save();Feedback(resumeNotice,"Настройка сохранена.");}
            catch(Exception error){prefs.AutoResumeDownloads=previous;resume.IsChecked=previous;Feedback(resumeNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        }
        resume.Click+=(_,_)=>SaveResume();
        Divider(files);
        var recovery=new CheckBox{Name="SettingsAutoRecover",Style=(Style)FindResource("SettingsSwitch"),Content=SwitchTitle("Восстанавливать подключения после обрыва сети"),IsChecked=prefs.AutoRecoverDownloads,Margin=new(0,0,0,8)};
        AutomationProperties.SetName(recovery,AppLanguage.T("Восстанавливать загрузки после обрыва сети"));files.Children.Add(recovery);
        files.Children.Add(Hint("Продолжим активные загрузки, когда вернётся сеть или компьютер проснётся."));
        var recoveryNotice=Notice(files);
        recovery.Click+=(_,_)=>
        {
            var previous=prefs.AutoRecoverDownloads;prefs.AutoRecoverDownloads=recovery.IsChecked==true;
            try{prefs.Save();Feedback(recoveryNotice,"Настройка сохранена.");if(prefs.AutoRecoverDownloads)RequestDownloadRecovery(true);}
            catch(Exception error){prefs.AutoRecoverDownloads=previous;recovery.IsChecked=previous;Feedback(recoveryNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        };
        Divider(files);
        files.Children.Add(Text("Свободное место",13));
        files.Children.Add(Hint("Перед загрузкой и во время неё проверяем место на диске и оставляем запас 256 МБ. Если места мало, загрузка встанет на паузу: освободи место и нажми «Продолжить»."));
        var notifications=Card("Уведомления","Сообщим, когда загрузка завершится, остановится с ошибкой или упрётся в нехватку места.","IconBell");
        var notify=new CheckBox{Name="SettingsNotifyDownloads",Style=(Style)FindResource("SettingsSwitch"),Content=SwitchTitle("Уведомления о загрузках"),IsChecked=prefs.NotifyDownloads,Margin=new(0,0,0,8)};
        AutomationProperties.SetName(notify,AppLanguage.T("Уведомления Windows о загрузках"));notifications.Children.Add(notify);
        notifications.Children.Add(Hint("Одинаковые ошибки не повторяются. Нажатие откроет очередь загрузок. В режиме «Не беспокоить» Windows уведомления скрывает."));
        var notificationNotice=Notice(notifications);
        notify.Click+=(_,_)=>
        {
            var previous=prefs.NotifyDownloads;prefs.NotifyDownloads=notify.IsChecked==true;
            try{prefs.Save();if(!prefs.NotifyDownloads)downloadNotifications?.Dismiss();Feedback(notificationNotice,"Настройка сохранена.");}
            catch(Exception error){prefs.NotifyDownloads=previous;notify.IsChecked=previous;Feedback(notificationNotice,"Не удалось сохранить настройки: "+error.Message,true);}
        };
        var testNotification=ActionButton("Проверить уведомление","IconInfo",()=>Feedback(notificationNotice,ShowTestDownloadNotification()?"Уведомление передано Windows. Если оно скрыто, проверь настройки уведомлений и режим «Не беспокоить».":prefs.NotifyDownloads?"Windows не приняла уведомление. Попробуй ещё раз.":"Включи уведомления о загрузках для проверки.",!prefs.NotifyDownloads));
        AutomationProperties.SetName(testNotification,AppLanguage.T("Проверить уведомление Windows"));notifications.Children.Add(testNotification);
        var system=Card("Запуск и Windows","Автозапуск и доступ в сеть для приёма раздач.","IconPower");
        var startupNotice=Notice(system);
        var startup=new CheckBox{Name="SettingsStartup",Content=SwitchTitle("Открывать Качалку при входе в Windows"),IsChecked=WindowsIntegration.StartupEnabled,Style=(Style)FindResource("SettingsSwitch"),Margin=new(0,0,0,12)};
        AutomationProperties.SetName(startup,AppLanguage.T("Запускать Качалку вместе с Windows"));system.Children.Add(startup);
        startup.Click+=(_,_)=>
        {
            try{WindowsIntegration.SetStartup(startup.IsChecked==true);Feedback(startupNotice,startup.IsChecked==true?"Качалка будет открываться при входе в Windows.":"Автозапуск выключен.");}
            catch(Exception error){startup.IsChecked=WindowsIntegration.StartupEnabled;Feedback(startupNotice,"Не удалось изменить автозапуск: "+error.Message,true);}
        };
        Divider(system);
        system.Children.Add(Text("Брандмауэр",13));
        system.Children.Add(Hint("Разрешает входящие TCP и UDP в частных сетях: так проще находить участников раздачи. Нужны права администратора."));
        var firewallNotice=Notice(system);
        var firewall=AsyncButton("Разрешить в брандмауэре",async()=>
        {
            try{Feedback(firewallNotice,await WindowsIntegration.AllowFirewallAsync()?"Исключение для Качалки добавлено.":"Запрос отменён. Настройки брандмауэра не изменены.");}
            catch(Exception error){ErrorLog.Write(error);Feedback(firewallNotice,error.Message,true);}
        });
        AutomationProperties.SetName(firewall,AppLanguage.T("Разрешить Качалку в брандмауэре"));firewall.HorizontalAlignment=HorizontalAlignment.Left;system.Children.Add(firewall);
        RenderLanSettings(Card("Устройства в сети","Отправляй загрузки на другой компьютер в домашней сети.","IconLaptop"));
        RenderUpdateSettings(Card("Обновления","Автоматическая проверка и установка новых версий.","IconRefresh"));
        var cache=Card("Кэш","Постеры, фото и данные каталога занимают до 1 ГБ. Лишнее удаляется само.","IconStorage");
        var cacheSize=Text("Подсчитываем размер…",13);cacheSize.Name="SettingsCacheSize";cache.Children.Add(cacheSize);
        var cacheNotice=Notice(cache);
        var clearCache=AsyncButton("Очистить кэш",async()=>
        {
            var result=await ClearCacheAsync();
            cacheSize.Text=AppLanguage.T("Занято "+CacheSize(result.Bytes)+" из 1 ГБ");
            Feedback(cacheNotice,"Освобождено "+CacheSize(result.FreedBytes)+(result.Failed>0?". Некоторые файлы заняты — очистка повторится автоматически.":"."));
        });
        clearCache.Name="ClearCacheButton";AutomationProperties.SetName(clearCache,AppLanguage.T("Очистить кэш"));clearCache.HorizontalAlignment=HorizontalAlignment.Left;cache.Children.Add(clearCache);
        cache.Children.Add(Hint("Скачанные файлы, очередь, «Сохранённое» и настройки не затрагиваются."));
        _=ShowCacheSizeAsync(cacheSize);
        var diagnostics=Card("Помощь и диагностика","Отчёт для поддержки. Личные пути и ключи доступа скрываются.","IconHelp");
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
        diagnostics.Children.Add(Text("Скопируй отчёт и приложи его к сообщению в чате поддержки.",11,true));
        var community=Card("Новости и поддержка","Новости Качалки и чат поддержки в Telegram.","IconBell");
        var communityActions=new WrapPanel();community.Children.Add(communityActions);
        var communityNotice=Notice(community);
        void OpenTelegram(string url)
        {
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}
            catch(Exception){Feedback(communityNotice,"Не удалось открыть Telegram. Попробуй открыть ссылку в браузере.",true);}
        }
        var news=ActionButton("Новости в Telegram","IconBell",()=>OpenTelegram("https://t.me/ka4alkacc"));
        news.ToolTip="https://t.me/ka4alkacc";AutomationProperties.SetName(news,AppLanguage.T("Новости в Telegram"));communityActions.Children.Add(news);
        var help=ActionButton("Сообщить об ошибке","IconHelp",()=>OpenTelegram("https://t.me/+6sT2ocaKvLxlNmRi"));
        help.ToolTip=AppLanguage.T("Открыть чат поддержки в Telegram");AutomationProperties.SetName(help,AppLanguage.T("Сообщить об ошибке"));communityActions.Children.Add(help);

        // Sections run from what people change often to what they set once or open when something goes wrong;
        // on wide windows the list on the left groups them the same way and jumps between the cards.
        var groups=new (string Caption,string[] Sections)[]{("Основное",["Загрузки","Внешний вид","Каталог","Игнорируемое","Уведомления"]),("Система",["Устройства в сети","Запуск и Windows","Обновления"]),("Обслуживание",["Новости и поддержка","Кэш","Помощь и диагностика"])};
        var wanted=groups.SelectMany(group=>group.Sections).ToArray();
        foreach(var name in wanted.Reverse())if(sectionFrames.TryGetValue(name,out var frame)){content.Children.Remove(frame);content.Children.Insert(0,frame);}
        Button? activeSection=null;
        void MarkSection(Button button)
        {
            if(activeSection!=null){activeSection.Background=Brushes.Transparent;activeSection.SetResourceReference(Control.ForegroundProperty,"Muted");activeSection.FontWeight=FontWeights.Medium;}
            activeSection=button;button.SetResourceReference(Control.BackgroundProperty,"NavSelected");button.SetResourceReference(Control.ForegroundProperty,"Text");button.FontWeight=FontWeights.SemiBold;
        }
        foreach(var (caption,sections) in groups)
        foreach(var name in sections)
        {
            if(!sectionFrames.TryGetValue(name,out var frame))continue;
            if(name==sections[0])
            {
                var label=Text(AppLanguage.T(caption).ToUpperInvariant(),11);label.FontWeight=FontWeights.SemiBold;label.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");
                label.Margin=new(14,sectionNav.Children.Count==0?4:18,0,6);sectionNav.Children.Add(label);
            }
            var jump=new Button{Style=(Style)FindResource("QuietButton"),Content=IconLabel(name,sectionIcons[name],17),HorizontalContentAlignment=HorizontalAlignment.Left,MinHeight=36,Height=36,Padding=new(14,0,14,0),Margin=new(0,0,0,2),FontSize=14,Tag=frame};
            jump.Click+=(_,_)=>
            {
                MarkSection(jump);settingsScroll.UpdateLayout();
                settingsScroll.ScrollToVerticalOffset(Math.Max(0,frame.TransformToAncestor(content).Transform(new Point()).Y));
            };
            AutomationProperties.SetName(jump,AppLanguage.T("Раздел: "+name));sectionNav.Children.Add(jump);if(activeSection==null)MarkSection(jump);
        }
        // The entry of the card under the reading line is highlighted while scrolling; at the very end it is the last one.
        var spy=new List<(Border Frame,Button Entry)>();
        foreach(var entry in sectionNav.Children.OfType<Button>())
            if(entry.Tag is Border owner)spy.Add((owner,entry));
        void SyncSection()
        {
            if(spy.Count==0)return;
            try
            {
                var line=settingsScroll.VerticalOffset+90;var current=spy[0];
                foreach(var row in spy)if(row.Frame.TransformToAncestor(content).Transform(new Point()).Y<=line)current=row;
                if(settingsScroll.ScrollableHeight>0&&settingsScroll.VerticalOffset>=settingsScroll.ScrollableHeight-2)current=spy[^1];
                if(!ReferenceEquals(activeSection,current.Entry))MarkSection(current.Entry);
            }
            catch(InvalidOperationException){}
        }
        settingsScroll.ScrollChanged+=(_,e)=>{if(e.VerticalChange!=0)SyncSection();};
        settingsLayout.SizeChanged+=(_,_)=>
        {
            var wide=settingsLayout.ActualWidth>=1000;
            sectionNav.Visibility=wide?Visibility.Visible:Visibility.Collapsed;settingsLayout.ColumnDefinitions[0].Width=wide?new GridLength(240):new GridLength(0);
        };
        var about=new WrapPanel{Margin=new(4,4,0,12)};content.Children.Add(about);
        var appName=Text("Качалка",13);appName.FontWeight=FontWeights.SemiBold;appName.Margin=new(0,0,10,4);about.Children.Add(appName);
        var version=new TextBlock{Text=AppLanguage.T("Версия "+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"")),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=12,Margin=new(0,1,0,4)};version.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");about.Children.Add(version);
    }
}
