using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    sealed class ReleasePickerState
    {
        public Dictionary<string,string> Filters {get;}=[];
        public string Sort {get;set;}="Русская озвучка";
        public bool More {get;set;}
        public HashSet<string> Expanded {get;}=[];
        public int VisibleCount {get;set;}=60;
        public bool OnlyRussian {get;set;}
        public bool Reverse {get;set;}
    }
    readonly Dictionary<int,ReleasePickerState> releasePickerStates=[];

    ReleasePickerState ReleaseSelection(int filmId)
    {
        if(releasePickerStates.TryGetValue(filmId,out var state))return state;
        if(releasePickerStates.Count>=40)releasePickerStates.Remove(releasePickerStates.Keys.First());
        return releasePickerStates[filmId]=new();
    }

    void RenderReleaseToolbar(StackPanel panel,MediaItem item)
    {
        var state=ReleaseSelection(item.Id);
        var toolbar=new WrapPanel{Name="ReleaseToolbar",Margin=new(0,0,0,8),VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(toolbar,"Фильтры и источники раздач");panel.Children.Add(toolbar);
        toolbar.Children.Add(QualityControls(()=>{state.VisibleCount=60;RefreshDetail(item.Id);}));
        RenderSourceStatus(toolbar,panel,item);
        RenderReleaseLoading(toolbar,item);
    }

    static int QualityRank(SourceEntry entry)=>entry.Quality switch{"4K"=>3,"Full HD"=>2,"HD Ready"=>1,_=>0};
    static int SeedRank(SourceEntry entry)=>entry.Seeds switch{>0=>entry.Seeds.Value,null=>0,_=>-1};
    static bool RussianSource(SourceEntry entry)=>entry.Source is "RuTor" or "RuTracker" or "NNM-Club" or "MegaPeer" or "BigFanGroup";
    static string VideoLabel(SourceEntry entry)
    {
        var parts=new[]{entry.Type,entry.Codec,entry.Hdr}.Where(x=>x!="Не указано").ToArray();
        return parts.Length==0?"Не указано":string.Join(" · ",parts);
    }

    // Small helpers shared by the table rows and the compact cards.
    FrameworkElement ReleaseTag(string text,bool accent=false,string? tooltip=null)
    {
        var label=new TextBlock{Text=text,FontSize=12,FontWeight=FontWeights.Medium,VerticalAlignment=VerticalAlignment.Center};
        label.SetResourceReference(TextBlock.ForegroundProperty,accent?"Accent":"Muted");
        var tag=new Border{Child=label,CornerRadius=new(6),Padding=new(8,2,8,2),Margin=new(0,0,6,4),MinHeight=22,ToolTip=tooltip};tag.SetResourceReference(Border.BackgroundProperty,"Raised");return tag;
    }
    FrameworkElement SeedHealth(SourceEntry entry)
    {
        var health=ReleaseRecommendation.HealthOf(entry.Seeds);var bars=ReleaseRecommendation.Bars(health);
        var key=health switch{ReleaseRecommendation.Health.Fast=>"Accent",ReleaseRecommendation.Health.Medium=>"Warning",ReleaseRecommendation.Health.Unknown=>"Subtle",_=>"Danger"};
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var meter=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,10,2)};
        for(var index=0;index<3;index++)
        {
            var bar=new Border{Width=4,Height=6+index*4,CornerRadius=new(1),Margin=new(index==0?0:2,0,0,0),VerticalAlignment=VerticalAlignment.Bottom};
            bar.SetResourceReference(Border.BackgroundProperty,index<bars?key:"TrackOff");meter.Children.Add(bar);
        }
        row.Children.Add(meter);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        var numbers=new StackPanel{Orientation=Orientation.Horizontal};
        var seeds=new TextBlock{Text=entry.Seeds.HasValue?string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:N0}",entry.Seeds.Value).Replace(',',' '):"—",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,FontWeight=FontWeights.SemiBold};seeds.SetResourceReference(TextBlock.ForegroundProperty,"Text");numbers.Children.Add(seeds);
        var peers=new TextBlock{Text="  ·  "+(entry.Leechers?.ToString()??"—"),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14};peers.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");numbers.Children.Add(peers);text.Children.Add(numbers);
        var caption=new TextBlock{Text=ReleaseRecommendation.Caption(health),FontSize=12};caption.SetResourceReference(TextBlock.ForegroundProperty,key);text.Children.Add(caption);
        row.Children.Add(text);
        row.ToolTip=$"Отдают: {entry.Seeds?.ToString()??"неизвестно"} · Скачивают: {entry.Leechers?.ToString()??"неизвестно"}. "+ReleaseFreshness.ConnectionNote;
        return row;
    }
    DownloadItem? QueuedDownload(SourceEntry entry)=>downloads.Items.FirstOrDefault(x=>x.ReleaseId==entry.Id&&x.ReleaseSource==entry.Source&&!string.IsNullOrEmpty(x.ReleaseId));
    // The row action: "Download" normally, a live status when the same release is already in the queue.
    FrameworkElement ReleaseAction(SourceEntry entry)
    {
        if(QueuedDownload(entry) is {} queued)
        {
            var frame=new Border{Width=150,Height=44,Cursor=System.Windows.Input.Cursors.Hand,Background=Brushes.Transparent,DataContext=queued,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Открыть загрузки"};
            var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            if(queued.Completed)
            {
                var pill=new Border{CornerRadius=new(999),Padding=new(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right};pill.SetResourceReference(Border.BackgroundProperty,"AccentSoft");
                var label=new TextBlock{Text="✓ Скачано",FontSize=13,FontWeight=FontWeights.SemiBold};label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");pill.Child=label;stack.Children.Add(pill);
            }
            else
            {
                var line=new DockPanel();var state=new TextBlock{Text="В загрузках",FontSize=13,FontWeight=FontWeights.SemiBold};state.SetResourceReference(TextBlock.ForegroundProperty,"Accent");line.Children.Add(state);
                var percent=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Right};percent.SetBinding(TextBlock.TextProperty,new Binding("PercentLabel"));percent.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(percent,Dock.Right);line.Children.Insert(0,percent);stack.Children.Add(line);
                var bar=new ProgressBar{Minimum=0,Maximum=100,Height=4,Margin=new(0,6,0,0),BorderThickness=new(0)};bar.SetResourceReference(Control.BackgroundProperty,"Track");bar.SetResourceReference(Control.ForegroundProperty,"Accent");bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new Binding("Progress"){Mode=BindingMode.OneWay});stack.Children.Add(bar);
            }
            frame.Child=stack;frame.MouseLeftButtonUp+=(_,_)=>{searchDelay.Stop();section="Загрузки";current=null;activePerson=null;downloadReturnItem=queued;Render();};
            AutomationProperties.SetName(frame,"Раздача уже в загрузках: "+entry.Title);return frame;
        }
        return LanDownloadAction(entry);
    }

    void RenderReleasePicker(StackPanel panel,IReadOnlyList<SourceEntry> releases)
    {
        var filmId=current?.Id??0;
        var state=ReleaseSelection(filmId);
        var host=releaseControlsHost??panel;
        var top=new WrapPanel{Name="ReleaseTopRow",Margin=new(0,0,0,6),VerticalAlignment=VerticalAlignment.Center};host.Children.Add(top);
        var seriesRow=new WrapPanel{Margin=new(0,0,0,0)};host.Children.Add(seriesRow);
        var more=new WrapPanel{Visibility=state.More?Visibility.Visible:Visibility.Collapsed,Margin=new(0,4,0,2)};host.Children.Add(more);
        // Quality and sort selectors stay in the tree (collapsed) because the segments and chips drive them.
        var hidden=new WrapPanel{Visibility=Visibility.Collapsed};host.Children.Add(hidden);
        var filters=new Dictionary<string,ComboBox>();
        void Filter(string label,IEnumerable<string> values,Panel target,bool captioned)
        {
            var field=new StackPanel{Width=captioned?156:120};
            if(captioned){var caption=new TextBlock{Text=label,FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,4)};caption.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");field.Children.Add(caption);}
            var options=new[]{"Все"}.Concat(values.Where(x=>!string.IsNullOrWhiteSpace(x))).Distinct().ToArray();
            var selected=state.Filters.GetValueOrDefault(label,"Все");
            if(!options.Contains(selected))selected="Все";
            var box=new ComboBox{ItemsSource=options,SelectedItem=selected,MinWidth=0,MinHeight=40,Margin=new(0,0,10,10)};
            AutomationProperties.SetName(box,"Раздачи: "+label);
            field.Children.Add(box);target.Children.Add(field);filters[label]=box;
            state.Filters[label]=selected;
        }
        var isSeries=current?.Section=="Сериалы";
        if(isSeries){Filter("Сезон",releases.Select(x=>x.Series).OrderBy(x=>x.Season??int.MaxValue).Select(x=>x.SeasonLabel),seriesRow,true);Filter("Серия",releases.Select(x=>x.Series).OrderBy(x=>x.Episode??int.MaxValue).Select(x=>x.EpisodeLabel),seriesRow,true);}
        Filter("Качество",releases.Select(x=>x.Quality),hidden,false);
        Filter("Озвучка",releases.Select(ReleaseFreshness.Audio),more,true);
        Filter("Субтитры",releases.Select(x=>x.Subs),more,true);
        Filter("Источник",releases.Select(x=>x.Source),more,true);
        Filter("Тип",releases.Select(x=>x.Type),more,true);
        Filter("Кодек",releases.Select(x=>x.Codec),more,true);
        Filter("HDR",releases.Select(x=>x.Hdr),more,true);
        var sorts=new[]{"Русская озвучка","Русские источники","Больше отдающих","Меньше размер","Выше качество","По названию","По видео","По субтитрам","По озвучке"}.Concat(isSeries?["По сезону и серии"]:[]).ToArray();
        var sort=new ComboBox{ItemsSource=sorts,SelectedItem=state.Sort,MinWidth=0,Margin=new(0,0,10,12)};
        if(sort.SelectedIndex<0)sort.SelectedIndex=0;
        AutomationProperties.SetName(sort,"Сортировка раздач");hidden.Children.Add(sort);

        // ---- quality segments ----
        var segmentFrame=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(4),Margin=new(0,0,10,10),VerticalAlignment=VerticalAlignment.Center};segmentFrame.SetResourceReference(Border.BackgroundProperty,"Panel");segmentFrame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
        AutomationProperties.SetName(segmentFrame,"Качество раздач");
        var segmentRow=new StackPanel{Orientation=Orientation.Horizontal};segmentFrame.Child=segmentRow;top.Children.Add(segmentFrame);
        var segments=new Dictionary<string,RadioButton>();var syncing=false;
        void Segment(string value,string label,int count)
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});
            content.Children.Add(new TextBlock{Text=count.ToString(),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=11,Opacity=.7,Margin=new(6,1,0,0),VerticalAlignment=VerticalAlignment.Center});
            var radio=new RadioButton{Style=(Style)FindResource("SegmentButton"),GroupName="ReleaseQuality"+filmId,Content=content,Tag=value,ToolTip="Показать раздачи: "+label};AutomationProperties.SetName(radio,"Качество: "+label);
            radio.Checked+=(_,_)=>{if(syncing)return;filters["Качество"].SelectedItem=value;};
            segments[value]=radio;segmentRow.Children.Add(radio);
        }
        // Segment counts follow the same pool as the list, so "Все" matches the number next to the section title.
        var pool=prefs.HidePoorQuality?releases.Where(x=>!ReleaseQuality.Poor(x,QualityMinimum)).ToArray():releases.ToArray();
        Segment("Все","Все",pool.Length);
        foreach(var group in pool.Where(x=>x.Quality!="Не указано").GroupBy(x=>x.Quality).OrderByDescending(x=>QualityRank(x.First())).ThenByDescending(x=>x.Count()))Segment(group.Key,group.Key,group.Count());

        // ---- Russian-audio switch ----
        var voice=new Button{Style=(Style)FindResource("PillButton"),Margin=new(0,0,10,10),Padding=new(14,0,16,0),ToolTip="Показывать только раздачи с профессиональной русской озвучкой"};
        var voiceTrack=new Border{Width=34,Height=20,CornerRadius=new(10),Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};var voiceKnob=new Border{Width=14,Height=14,CornerRadius=new(7),Margin=new(3),HorizontalAlignment=HorizontalAlignment.Left};voiceTrack.Child=voiceKnob;
        var voiceRow=new StackPanel{Orientation=Orientation.Horizontal};voiceRow.Children.Add(voiceTrack);voiceRow.Children.Add(new TextBlock{Text="Только русская озвучка",VerticalAlignment=VerticalAlignment.Center});voice.Content=voiceRow;
        AutomationProperties.SetName(voice,"Только русская озвучка");top.Children.Add(voice);

        // ---- sort chip ----
        var sortChip=new Button{Style=(Style)FindResource("PillButton"),Margin=new(0,0,10,10)};AutomationProperties.SetName(sortChip,"Сортировка раздач: выбрать");top.Children.Add(sortChip);
        var sortMenu=new ToggleContextMenu{PlacementTarget=sortChip,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,MaxHeight=380};
        foreach(var name in sorts){var entry=new MenuItem{Header=name,IsCheckable=true,Tag=name};entry.Click+=(_,_)=>{sortMenu.IsOpen=false;sort.SelectedItem=name;};sortMenu.Items.Add(entry);}
        AttachMenuToggle(sortChip,sortMenu);

        var toggle=new Button{Style=(Style)FindResource("PillButton"),Margin=new(0,0,10,10)};
        var reset=new Button{Content="Сбросить",Style=(Style)FindResource("QuietButton"),Padding=new(12,0,12,0),Margin=new(0,0,10,10)};
        AutomationProperties.SetName(reset,"Сбросить фильтры и сортировку раздач");
        top.Children.Add(toggle);top.Children.Add(reset);
        var results=new StackPanel{Margin=new(0,8,0,0)};panel.Children.Add(results);
        var recommended=ReleaseRecommendation.Pick(releases,QualityMinimum);
        bool compact=Body.ActualWidth<1000,changingFilters=false;

        // Refresh, sources and the poor-quality switch stay out of the way until "Ещё фильтры" is opened
        // (or a source scan is running, so progress is never hidden).
        var toolbarRow=FindVisual<WrapPanel>(panel,x=>x.Name=="ReleaseToolbar");
        void ApplyMore()
        {
            more.Visibility=state.More?Visibility.Visible:Visibility.Collapsed;
            if(toolbarRow!=null)toolbarRow.Visibility=state.More||(releaseViews.TryGetValue(filmId,out var scan)&&scan.Checking)?Visibility.Visible:Visibility.Collapsed;
        }
        ApplyMore();
        void UpdateActions()
        {
            var additional=new[]{"Озвучка","Источник","Тип","Кодек","HDR","Субтитры"}.Count(x=>filters[x].SelectedItem?.ToString()!="Все");
            toggle.Content=ChipContent((state.More?"Скрыть фильтры":"Ещё фильтры")+(additional>0?$" · {additional}":""),true);StyleChip(toggle,additional>0);
            AutomationProperties.SetName(toggle,state.More?"Скрыть дополнительные фильтры раздач":"Показать дополнительные фильтры раздач");
            reset.Visibility=prefs.HidePoorQuality||state.OnlyRussian||filters.Values.Any(x=>x.SelectedItem?.ToString()!="Все")||sort.SelectedIndex!=0?Visibility.Visible:Visibility.Collapsed;
            syncing=true;var quality=filters["Качество"].SelectedItem?.ToString()??"Все";foreach(var pair in segments)pair.Value.IsChecked=pair.Key==quality;syncing=false;
            voiceTrack.SetResourceReference(Border.BackgroundProperty,state.OnlyRussian?"Accent":"TrackOff");voiceKnob.SetResourceReference(Border.BackgroundProperty,state.OnlyRussian?"AccentInk":"Muted");voiceKnob.HorizontalAlignment=state.OnlyRussian?HorizontalAlignment.Right:HorizontalAlignment.Left;
            StyleChip(voice,state.OnlyRussian);
            var current=sort.SelectedItem?.ToString()??sorts[0];sortChip.Content=ChipContent(current,true,"IconFilter");StyleChip(sortChip,false);
            foreach(MenuItem entry in sortMenu.Items)entry.IsChecked=Equals(entry.Tag,current);
        }
        void SaveSelection()
        {
            foreach(var pair in filters)state.Filters[pair.Key]=pair.Value.SelectedItem?.ToString()??"Все";
            state.Sort=sort.SelectedItem?.ToString()??"Русская озвучка";
        }
        Grid RowGrid()
        {
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new(){Width=new GridLength(96)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=240});
            grid.ColumnDefinitions.Add(new(){Width=new GridLength(88)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(156)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(120)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(176)});
            return grid;
        }
        string QualityLabel(SourceEntry entry)=>ReleaseQuality.Height(entry) is int height?height+"p":entry.Quality;
        FrameworkElement DownloadButton(SourceEntry entry)=>ReleaseAction(entry);
        FrameworkElement CompactRelease(SourceEntry entry)
        {
            var isBest=recommended!=null&&ReferenceEquals(recommended.Entry,entry);
            var body=new StackPanel();
            if(isBest)body.Children.Add(RecommendedPill());
            var title=Text(entry.Title,14);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,8);body.Children.Add(title);
            var source=Text(ReleaseFreshness.Caption(entry,DateTime.UtcNow),12,true);source.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");source.ToolTip=ReleaseFreshness.Details(entry,DateTime.UtcNow);source.Margin=new(0,0,0,12);body.Children.Add(source);
            var summary=new WrapPanel{Margin=new(0,0,0,6)};
            if(ReleaseQuality.Poor(entry,QualityMinimum))summary.Children.Add(PoorQualityBadge(ReleaseQuality.Label(entry)));
            if(entry.Quality!="Не указано")summary.Children.Add(ReleaseTag(entry.Quality,true));
            if(ReleaseFreshness.Audio(entry)!="Не указано")summary.Children.Add(ReleaseTag(ReleaseFreshness.Audio(entry),RussianAudio.Rank(entry)==3,ReleaseFreshness.AudioNote));
            if(isSeries){summary.Children.Add(ReleaseTag(entry.Series.SeasonLabel));summary.Children.Add(ReleaseTag(entry.Series.EpisodeLabel));}
            summary.Children.Add(ReleaseTag(entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"Размер не указан"));
            summary.Children.Add(ReleaseTag(entry.Seeds switch{>0=> $"Отдают: {entry.Seeds.Value}",0=>"Отдают: 0 по данным источника",_=>"Отдают: неизвестно"},false,ReleaseFreshness.ConnectionNote));
            if(entry.Leechers.HasValue)summary.Children.Add(ReleaseTag($"Скачивают: {entry.Leechers.Value}",false,ReleaseFreshness.ConnectionNote));
            body.Children.Add(summary);
            if(isBest)body.Children.Add(ReasonLine(recommended!.Reason));
            if(entry.Seeds is >0 and <10)body.Children.Add(LowSeedLine());
            var key=entry.Source+"|"+entry.Id;
            var details=new StackPanel{Visibility=state.Expanded.Contains(key)?Visibility.Visible:Visibility.Collapsed,Margin=new(0,0,0,12)};
            foreach(var value in new[]{"Видео: "+VideoLabel(entry),"Озвучка: "+ReleaseFreshness.Audio(entry),"Субтитры: "+entry.Subs})
            {
                var line=Text(value,13,true);line.Margin=new(0,0,0,6);details.Children.Add(line);
            }
            body.Children.Add(details);
            var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left};
            var expand=new Button{Content=state.Expanded.Contains(key)?"Скрыть детали":"Подробнее",Style=(Style)FindResource("QuietButton"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0),Padding=new(10,0,10,0)};
            AutomationProperties.SetName(expand,"Показать или скрыть параметры раздачи: "+entry.Title);
            expand.Click+=(_,_)=>
            {
                var open=details.Visibility!=Visibility.Visible;
                details.Visibility=open?Visibility.Visible:Visibility.Collapsed;expand.Content=open?"Скрыть детали":"Подробнее";
                if(open)state.Expanded.Add(key);else state.Expanded.Remove(key);
            };
            footer.Children.Add(expand);var download=DownloadButton(entry);footer.Children.Add(download);body.Children.Add(footer);
            var card=new Border{Child=body,CornerRadius=new(16),Padding=new(16),Margin=new(0,0,0,8),BorderThickness=new(1)};
            card.SetResourceReference(Border.BackgroundProperty,isBest?"AccentFaint":"Panel");card.SetResourceReference(Border.BorderBrushProperty,isBest?"AccentLine":"EdgeSoft");return card;
        }
        FrameworkElement RecommendedPill()
        {
            var pill=new Border{CornerRadius=new(999),Padding=new(9,0,9,0),Height=22,Margin=new(0,0,10,6),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};pill.SetResourceReference(Border.BackgroundProperty,"AccentSoft");
            var label=new TextBlock{Text="★ Рекомендуем",FontSize=11,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");pill.Child=label;return pill;
        }
        FrameworkElement ReasonLine(string reason)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,4,0,0)};
            var icon=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconInfo"),Width=14,Height=14,Stretch=Stretch.Uniform,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Muted");row.Children.Add(icon);
            var text=new TextBlock{Text=reason,FontSize=12,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");row.Children.Add(text);return row;
        }
        FrameworkElement LowSeedLine()
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,4,0,0)};
            var icon=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconAlert"),Width=14,Height=14,Stretch=Stretch.Uniform,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Danger");row.Children.Add(icon);
            var text=new TextBlock{Text="Мало сидов — загрузка может идти медленно",FontSize=12,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"Danger");row.Children.Add(text);return row;
        }
        void Show()
        {
            SaveSelection();UpdateActions();results.Children.Clear();
            IEnumerable<SourceEntry> filtered=releases;
            if(prefs.HidePoorQuality)filtered=filtered.Where(x=>!ReleaseQuality.Poor(x,QualityMinimum));
            foreach(var key in new[]{"Источник","Сезон","Серия","Качество","Тип","Озвучка","Субтитры","Кодек","HDR"})
            {
                if(!filters.ContainsKey(key))continue;
                var value=state.Filters[key];if(value=="Все")continue;
                filtered=key switch{"Источник"=>filtered.Where(x=>x.Source==value),"Сезон"=>filtered.Where(x=>x.Series.SeasonLabel==value),"Серия"=>filtered.Where(x=>x.Series.EpisodeLabel==value),"Качество"=>filtered.Where(x=>x.Quality==value),"Тип"=>filtered.Where(x=>x.Type==value),"Озвучка"=>filtered.Where(x=>ReleaseFreshness.Audio(x)==value),"Субтитры"=>filtered.Where(x=>x.Subs==value),"Кодек"=>filtered.Where(x=>x.Codec==value),_=>filtered.Where(x=>x.Hdr==value)};
            }
            if(state.OnlyRussian)filtered=filtered.Where(x=>RussianAudio.Rank(x)==3);
            filtered=state.Sort switch{"Русская озвучка"=>RussianAudio.Order(filtered),"Русские источники"=>filtered.OrderByDescending(RussianSource).ThenByDescending(SeedRank),"Меньше размер"=>filtered.OrderBy(x=>x.Size??long.MaxValue),"Выше качество"=>filtered.OrderByDescending(QualityRank).ThenByDescending(SeedRank),"По названию"=>filtered.OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase),"По сезону и серии"=>filtered.OrderBy(x=>x.Series.Season??int.MaxValue).ThenBy(x=>x.Series.Episode??int.MaxValue),"По видео"=>filtered.OrderBy(x=>x.Type=="Не указано").ThenBy(x=>x.Type),"По субтитрам"=>filtered.OrderBy(x=>x.Subs=="Не указано").ThenBy(x=>x.Subs),"По озвучке"=>filtered.OrderByDescending(x=>RussianAudio.Rank(x)).ThenBy(x=>ReleaseFreshness.Audio(x)),_=>filtered.OrderByDescending(SeedRank)};
            if(state.Reverse)filtered=filtered.Reverse();
            var visible=filtered.ToArray();var displayed=visible.Take(state.VisibleCount).ToArray();
            if(releaseCountLabel!=null)releaseCountLabel.Text=visible.Length.ToString();
            // The count already sits next to the section title and the list ends with "Показать ещё", so no extra notes here;
            // the seed disclaimer stays in the row tooltips.
            if(visible.Length==0)
            {
                var empty=new StackPanel{Margin=new(20)};
                var title=Text("Таких раздач пока нет",16);title.FontWeight=FontWeights.SemiBold;empty.Children.Add(title);
                empty.Children.Add(Text("Измени параметры или сбрось фильтры, чтобы увидеть все варианты.",13,true));
                var surface=new Border{Child=empty,CornerRadius=new(16),BorderThickness=new(1)};surface.SetResourceReference(Border.BackgroundProperty,"Panel");surface.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");results.Children.Add(surface);return;
            }
            void MoreResults()
            {
                if(displayed.Length==visible.Length)return;
                var next=Button($"Показать ещё · осталось {visible.Length-displayed.Length}",()=>{state.VisibleCount+=60;Show();});next.HorizontalAlignment=HorizontalAlignment.Left;next.Margin=new(0,4,0,24);results.Children.Add(next);
            }
            if(compact)
            {
                foreach(var entry in displayed)results.Children.Add(CompactRelease(entry));MoreResults();
                return;
            }
            var header=RowGrid();header.Margin=new(17,0,17,8);
            void Heading(string label,int column,string? mode)
            {
                if(mode==null)
                {
                    var plain=new TextBlock{Text=label,FontSize=11,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};plain.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");Grid.SetColumn(plain,column);header.Children.Add(plain);return;
                }
                var active=state.Sort==mode;
                var descendingByDefault=mode is "Больше отдающих" or "Выше качество";
                var direction=descendingByDefault^(active&&state.Reverse)?" ↓":" ↑";
                var button=new Button{Style=(Style)FindResource("LinkButton"),Content=label+(active?direction:""),FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new(0,6,0,6),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,ToolTip=active?"Нажми ещё раз, чтобы поменять порядок":"Сортировать по этому столбцу"};
                // Clicking the active column flips its direction; any other column makes itself the order.
                button.Click+=(_,_)=>{if(sort.SelectedItem?.ToString()==mode){state.Reverse=!state.Reverse;state.VisibleCount=60;Show();}else sort.SelectedItem=mode;};
                button.SetResourceReference(Control.ForegroundProperty,active?"Accent":"Subtle");AutomationProperties.SetName(button,"Сортировать раздачи: "+mode);
                Grid.SetColumn(button,column);header.Children.Add(button);
            }
            Heading("КАЧЕСТВО",0,"Выше качество");Heading("РАЗДАЧА",1,"По названию");Heading("РАЗМЕР",2,"Меньше размер");Heading("СИДЫ · ПИРЫ",3,"Больше отдающих");Heading("ДОБАВЛЕНА",4,null);
            results.Children.Add(header);
            foreach(var entry in displayed)
            {
                var isBest=recommended!=null&&ReferenceEquals(recommended.Entry,entry);
                var row=RowGrid();
                var quality=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
                var big=new TextBlock{Text=QualityLabel(entry),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=16,FontWeight=FontWeights.SemiBold};quality.Children.Add(big);
                var under=entry.Hdr!="Не указано"?entry.Hdr:entry.Type!="Не указано"?entry.Type:"";
                if(under.Length>0){var small=new TextBlock{Text=under,FontSize=12,Margin=new(0,2,0,0)};small.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");quality.Children.Add(small);}
                row.Children.Add(quality);
                var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};Grid.SetColumn(identity,1);
                var titleLine=new DockPanel();if(isBest){var pill=RecommendedPill();pill.Margin=new(0,0,10,0);DockPanel.SetDock(pill,Dock.Left);titleLine.Children.Add(pill);}
                var title=new TextBlock{Text=entry.Title,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=entry.Title,VerticalAlignment=VerticalAlignment.Center};titleLine.Children.Add(title);identity.Children.Add(titleLine);
                var tags=new WrapPanel{Margin=new(0,6,0,0)};
                if(ReleaseQuality.Poor(entry,QualityMinimum))tags.Children.Add(PoorQualityBadge(ReleaseQuality.Label(entry)));
                if(ReleaseFreshness.Audio(entry)!="Не указано")tags.Children.Add(ReleaseTag(ReleaseFreshness.Audio(entry),RussianAudio.Rank(entry)==3,ReleaseFreshness.AudioNote));
                if(entry.Subs!="Не указано")tags.Children.Add(ReleaseTag("Субтитры"));
                if(VideoLabel(entry)!="Не указано")tags.Children.Add(ReleaseTag(VideoLabel(entry)));
                if(isSeries)tags.Children.Add(ReleaseTag(entry.Series.SeasonLabel+" · "+entry.Series.EpisodeLabel));
                if(tags.Children.Count>0)identity.Children.Add(tags);
                if(isBest)identity.Children.Add(ReasonLine(recommended!.Reason));
                if(entry.Seeds is >0 and <10)identity.Children.Add(LowSeedLine());
                row.Children.Add(identity);
                var size=new TextBlock{Text=entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"—",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(size,2);row.Children.Add(size);
                var health=SeedHealth(entry);Grid.SetColumn(health,3);row.Children.Add(health);
                var age=new StackPanel{VerticalAlignment=VerticalAlignment.Center,ToolTip=ReleaseFreshness.Details(entry,DateTime.UtcNow)};Grid.SetColumn(age,4);
                var when=new TextBlock{Text=ReleaseFreshness.Age(entry.DataReceivedUtc,DateTime.UtcNow),FontSize=13};when.SetResourceReference(TextBlock.ForegroundProperty,"Muted");age.Children.Add(when);
                var origin=new TextBlock{Text=ReleaseFreshness.SourceName(entry)+(ReleaseFreshness.FromIndex(entry)?" · индекс":""),FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,2,6,0)};origin.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");age.Children.Add(origin);row.Children.Add(age);
                var download=DownloadButton(entry);download.HorizontalAlignment=HorizontalAlignment.Right;download.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(download,5);row.Children.Add(download);
                var rowFrame=new Border{Child=row,CornerRadius=new(16),BorderThickness=new(1),Padding=new(16,12,16,12),Margin=new(0,0,0,8)};
                rowFrame.SetResourceReference(Border.BackgroundProperty,isBest?"AccentFaint":"Panel");rowFrame.SetResourceReference(Border.BorderBrushProperty,isBest?"AccentLine":"EdgeSoft");results.Children.Add(rowFrame);
            }
            MoreResults();
        }
        foreach(var box in filters.Values)box.SelectionChanged+=(_,_)=>{if(!changingFilters){state.VisibleCount=60;Show();}};
        sort.SelectionChanged+=(_,_)=>{if(!changingFilters){state.Reverse=false;state.VisibleCount=60;Show();}};
        voice.Click+=(_,_)=>{state.OnlyRussian=!state.OnlyRussian;state.VisibleCount=60;Show();};
        toggle.Click+=(_,_)=>{state.More=!state.More;ApplyMore();UpdateActions();};
        reset.Click+=(_,_)=>{changingFilters=true;foreach(var box in filters.Values)box.SelectedIndex=0;sort.SelectedIndex=0;state.OnlyRussian=false;state.Reverse=false;changingFilters=false;state.VisibleCount=60;prefs.HidePoorQuality=false;prefs.Save();SaveSelection();Render();};
        panel.SizeChanged+=(_,_)=>{var next=Body.ActualWidth<1000;if(next==compact)return;compact=next;Show();};
        Show();
    }
}
