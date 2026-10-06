using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    sealed class ReleasePickerState
    {
        public Dictionary<string,string> Filters {get;}=[];
        public string Sort {get;set;}="Больше сидов";
        public bool More {get;set;}
        public HashSet<string> Expanded {get;}=[];
        public int VisibleCount {get;set;}=60;
    }
    readonly Dictionary<int,ReleasePickerState> releasePickerStates=[];

    static int QualityRank(SourceEntry entry)=>entry.Quality switch{"2160p"=>3,"1080p"=>2,"720p"=>1,_=>0};
    static int SeedRank(SourceEntry entry)=>entry.Seeds switch{>0=>entry.Seeds.Value,null=>0,_=>-1};
    static string VideoLabel(SourceEntry entry)
    {
        var parts=new[]{entry.Type,entry.Codec,entry.Hdr}.Where(x=>x!="Не указано").ToArray();
        return parts.Length==0?"Не указано":string.Join(" · ",parts);
    }

    void RenderReleasePicker(StackPanel panel,IReadOnlyList<SourceEntry> releases)
    {
        var filmId=current?.Id??0;
        if(!releasePickerStates.TryGetValue(filmId,out var state))
        {
            if(releasePickerStates.Count>=40)releasePickerStates.Remove(releasePickerStates.Keys.First());
            state=new();releasePickerStates[filmId]=state;
        }
        var controls=new WrapPanel{Margin=new(0,4,0,0)};panel.Children.Add(controls);
        var more=new WrapPanel{Visibility=state.More?Visibility.Visible:Visibility.Collapsed,Margin=new(0,0,0,2)};panel.Children.Add(more);
        var filters=new Dictionary<string,ComboBox>();
        void Filter(string label,IEnumerable<string> values,bool advanced=false)
        {
            var field=new StackPanel{Width=156};
            var caption=Text(label,11,true);caption.Margin=new(0,0,0,4);field.Children.Add(caption);
            var options=new[]{"Все"}.Concat(values.Where(x=>!string.IsNullOrWhiteSpace(x))).Distinct().ToArray();
            var selected=state.Filters.GetValueOrDefault(label,"Все");
            if(!options.Contains(selected))selected="Все";
            var box=new ComboBox{ItemsSource=options,SelectedItem=selected,MinWidth=0,Margin=new(0,0,10,12)};
            AutomationProperties.SetName(box,"Раздачи: "+label);
            field.Children.Add(box);(advanced?more:controls).Children.Add(field);filters[label]=box;
            state.Filters[label]=selected;
        }
        var isSeries=current?.Section=="Сериалы";
        if(isSeries){Filter("Сезон",releases.Select(x=>x.Series).OrderBy(x=>x.Season??int.MaxValue).Select(x=>x.SeasonLabel));Filter("Серия",releases.Select(x=>x.Series).OrderBy(x=>x.Episode??int.MaxValue).Select(x=>x.EpisodeLabel));}
        Filter("Качество",releases.Select(x=>x.Quality));
        Filter("Озвучка",releases.Select(x=>x.Voice));
        Filter("Субтитры",releases.Select(x=>x.Subs));
        Filter("Источник",releases.Select(x=>x.Source),true);
        Filter("Тип",releases.Select(x=>x.Type),true);
        Filter("Кодек",releases.Select(x=>x.Codec),true);
        Filter("HDR",releases.Select(x=>x.Hdr),true);
        var sortField=new StackPanel{Width=176};
        var sortCaption=Text("Сортировка",11,true);sortCaption.Margin=new(0,0,0,4);sortField.Children.Add(sortCaption);
        var sorts=new[]{"Больше сидов","Меньше размер","Выше качество","По названию","По видео","По субтитрам","По озвучке"}.Concat(isSeries?["По сезону и серии"]:[]).ToArray();
        var sort=new ComboBox{ItemsSource=sorts,SelectedItem=state.Sort,MinWidth=0,Margin=new(0,0,10,12)};
        if(sort.SelectedIndex<0)sort.SelectedIndex=0;
        AutomationProperties.SetName(sort,"Сортировка раздач");
        sortField.Children.Add(sort);controls.Children.Add(sortField);

        var actions=new WrapPanel{Margin=new(0,0,0,12)};panel.Children.Add(actions);
        var toggle=new Button{Style=(Style)FindResource("QuietButton"),Padding=new(0,6,12,6),Margin=new(0,0,12,0),HorizontalAlignment=HorizontalAlignment.Left,BorderThickness=new(0)};
        var reset=new Button{Content="Сбросить",Style=(Style)FindResource("QuietButton"),Padding=new(10,6,10,6),Margin=new(0),BorderThickness=new(0)};
        AutomationProperties.SetName(reset,"Сбросить фильтры и сортировку раздач");
        actions.Children.Add(toggle);actions.Children.Add(reset);
        var results=new StackPanel();panel.Children.Add(results);
        bool compact=Body.ActualWidth<1000,changingFilters=false;

        void UpdateActions()
        {
            var additional=new[]{"Источник","Тип","Кодек","HDR"}.Count(x=>filters[x].SelectedItem?.ToString()!="Все");
            toggle.Content=state.More?"Скрыть дополнительные фильтры":"Ещё фильтры"+(additional>0?$" · {additional}":"");
            AutomationProperties.SetName(toggle,state.More?"Скрыть дополнительные фильтры раздач":"Показать дополнительные фильтры раздач");
            reset.Visibility=filters.Values.Any(x=>x.SelectedItem?.ToString()!="Все")||sort.SelectedIndex!=0?Visibility.Visible:Visibility.Collapsed;
        }
        void SaveSelection()
        {
            foreach(var pair in filters)state.Filters[pair.Key]=pair.Value.SelectedItem?.ToString()??"Все";
            state.Sort=sort.SelectedItem?.ToString()??"Больше сидов";
        }
        Grid RowGrid()
        {
            var grid=new Grid();
            foreach(var weight in new[]{1.75,.55,.7,1.2,.85,1.2,.7})grid.ColumnDefinitions.Add(new(){Width=new GridLength(weight,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new(){Width=new GridLength(112)});
            return grid;
        }
        TextBlock Cell(string value,int column,bool muted=false)
        {
            var label=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,Margin=new(10,15,10,15),VerticalAlignment=VerticalAlignment.Center,FontSize=12};
            label.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Text");Grid.SetColumn(label,column);return label;
        }
        Button DownloadButton(SourceEntry entry)
        {
            var download=new Button{Content="Скачать",Tag=entry,Style=(Style)FindResource("PrimaryButton"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new(0),Padding=new(13,9,13,9)};
            AutomationProperties.SetName(download,"Скачать раздачу: "+entry.Title);
            download.Click+=SourceDownload;return download;
        }
        FrameworkElement CompactRelease(SourceEntry entry)
        {
            var body=new StackPanel();
            var title=Text(entry.Title,14);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,8);body.Children.Add(title);
            var source=Text(entry.Source+(entry.Via==null?"":" · через "+entry.Via),11,true);source.Margin=new(0,0,0,12);body.Children.Add(source);
            var summary=new WrapPanel{Margin=new(0,0,0,8)};
            void Badge(string value,bool accent=false)
            {
                var text=new TextBlock{Text=value,FontSize=11,FontWeight=accent?FontWeights.SemiBold:FontWeights.Normal};
                text.SetResourceReference(TextBlock.ForegroundProperty,accent?"Accent":"Text");
                var badge=new Border{Child=text,CornerRadius=new(7),Padding=new(8,5,8,5),Margin=new(0,0,6,6)};
                badge.SetResourceReference(Border.BackgroundProperty,"Selected");summary.Children.Add(badge);
            }
            if(entry.Quality!="Не указано")Badge(entry.Quality,true);
            if(isSeries){Badge(entry.Series.SeasonLabel);Badge(entry.Series.EpisodeLabel);}
            Badge(entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"Размер не указан");
            Badge(entry.Seeds switch{>0=>$"Сиды: {entry.Seeds.Value}",0=>"Сидов нет по данным источника",_=>"Сиды: неизвестно"});
            body.Children.Add(summary);
            var key=entry.Source+"|"+entry.Id;
            var details=new StackPanel{Visibility=state.Expanded.Contains(key)?Visibility.Visible:Visibility.Collapsed,Margin=new(0,0,0,12)};
            foreach(var value in new[]{"Видео: "+VideoLabel(entry),"Озвучка: "+entry.Voice,"Субтитры: "+entry.Subs})
            {
                var line=Text(value,12,true);line.Margin=new(0,0,0,6);details.Children.Add(line);
            }
            body.Children.Add(details);
            var footer=new Grid();footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var expand=new Button{Content=state.Expanded.Contains(key)?"Скрыть детали":"Подробнее",Style=(Style)FindResource("QuietButton"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new(0),Padding=new(0,8,8,8),BorderThickness=new(0)};
            AutomationProperties.SetName(expand,"Показать или скрыть параметры раздачи: "+entry.Title);
            expand.Click+=(_,_)=>
            {
                var open=details.Visibility!=Visibility.Visible;
                details.Visibility=open?Visibility.Visible:Visibility.Collapsed;expand.Content=open?"Скрыть детали":"Подробнее";
                if(open)state.Expanded.Add(key);else state.Expanded.Remove(key);
            };
            footer.Children.Add(expand);var download=DownloadButton(entry);Grid.SetColumn(download,1);footer.Children.Add(download);body.Children.Add(footer);
            var card=new Border{Child=body,CornerRadius=new(14),Padding=new(16),Margin=new(0,0,0,10),BorderThickness=new(1)};
            card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Edge");return card;
        }
        void Show()
        {
            SaveSelection();UpdateActions();results.Children.Clear();
            IEnumerable<SourceEntry> filtered=releases;
            foreach(var key in new[]{"Источник","Сезон","Серия","Качество","Тип","Озвучка","Субтитры","Кодек","HDR"})
            {
                if(!filters.ContainsKey(key))continue;
                var value=state.Filters[key];if(value=="Все")continue;
                filtered=key switch{"Источник"=>filtered.Where(x=>x.Source==value),"Сезон"=>filtered.Where(x=>x.Series.SeasonLabel==value),"Серия"=>filtered.Where(x=>x.Series.EpisodeLabel==value),"Качество"=>filtered.Where(x=>x.Quality==value),"Тип"=>filtered.Where(x=>x.Type==value),"Озвучка"=>filtered.Where(x=>x.Voice==value),"Субтитры"=>filtered.Where(x=>x.Subs==value),"Кодек"=>filtered.Where(x=>x.Codec==value),_=>filtered.Where(x=>x.Hdr==value)};
            }
            filtered=state.Sort switch{"Меньше размер"=>filtered.OrderBy(x=>x.Size??long.MaxValue),"Выше качество"=>filtered.OrderByDescending(QualityRank).ThenByDescending(SeedRank),"По названию"=>filtered.OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase),"По сезону и серии"=>filtered.OrderBy(x=>x.Series.Season??int.MaxValue).ThenBy(x=>x.Series.Episode??int.MaxValue),"По видео"=>filtered.OrderBy(x=>x.Type=="Не указано").ThenBy(x=>x.Type),"По субтитрам"=>filtered.OrderBy(x=>x.Subs=="Не указано").ThenBy(x=>x.Subs),"По озвучке"=>filtered.OrderBy(x=>x.Voice=="Не указано").ThenBy(x=>x.Voice),_=>filtered.OrderByDescending(SeedRank)};
            var visible=filtered.ToArray();var displayed=visible.Take(state.VisibleCount).ToArray();
            var countLabel=displayed.Length<visible.Length?$"Показано {displayed.Length} из {visible.Length}":visible.Length<releases.Count?$"Вариантов: {visible.Length} из {releases.Count}":$"Вариантов: {visible.Length}";
            var count=Text(countLabel,12,true);count.Margin=new(0,0,0,12);results.Children.Add(count);
            if(visible.Any(x=>x.Seeds==0)){var warning=Text("У некоторых раздач источник показывает 0 сидов — они могут не скачаться. Сначала попробуй варианты с доступными участниками.",12,true);warning.Margin=new(0,0,0,12);results.Children.Add(warning);}
            if(visible.Length==0)
            {
                var empty=new StackPanel{Margin=new(20)};
                var title=Text("Таких раздач пока нет",16);title.FontWeight=FontWeights.SemiBold;empty.Children.Add(title);
                empty.Children.Add(Text("Измени параметры или сбрось фильтры, чтобы увидеть все варианты.",12,true));
                var surface=new Border{Child=empty,CornerRadius=new(14),BorderThickness=new(1)};surface.SetResourceReference(Border.BackgroundProperty,"Panel");surface.SetResourceReference(Border.BorderBrushProperty,"Edge");results.Children.Add(surface);return;
            }
            void MoreResults()
            {
                if(displayed.Length==visible.Length)return;
                var next=Button($"Показать ещё · осталось {visible.Length-displayed.Length}",()=>{state.VisibleCount+=60;Show();});next.Style=(Style)FindResource("QuietButton");next.HorizontalAlignment=HorizontalAlignment.Left;next.Margin=new(0,4,0,20);results.Children.Add(next);
            }
            if(compact)
            {
                foreach(var entry in displayed)results.Children.Add(CompactRelease(entry));MoreResults();
                return;
            }
            var table=new StackPanel();var header=RowGrid();
            void Heading(string label,int column,string mode)
            {
                var active=state.Sort==mode;
                var direction=mode is "Больше сидов" or "Выше качество"?" ↓":" ↑";
                var button=Button(label+(active?direction:""),()=>sort.SelectedItem=mode);
                button.Style=(Style)FindResource("QuietButton");button.BorderThickness=new(0);button.Padding=new(10,12,3,12);button.Margin=new(0);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.FontSize=11;button.FontWeight=active?FontWeights.SemiBold:FontWeights.Medium;
                button.SetResourceReference(Control.ForegroundProperty,active?"Accent":"Muted");AutomationProperties.SetName(button,"Сортировать раздачи: "+mode);
                Grid.SetColumn(button,column);header.Children.Add(button);
            }
            Heading("Раздача",0,"По названию");Heading("Сиды",1,"Больше сидов");Heading("Качество",2,"Выше качество");
            Heading("Видео",3,"По видео");Heading("Субтитры",4,"По субтитрам");Heading("Озвучка",5,"По озвучке");Heading("Объём",6,"Меньше размер");
            var headerFrame=new Border{Child=header,CornerRadius=new(12,12,0,0),BorderThickness=new(0,0,0,1)};headerFrame.SetResourceReference(Border.BackgroundProperty,"Sidebar");headerFrame.SetResourceReference(Border.BorderBrushProperty,"Edge");table.Children.Add(headerFrame);
            foreach(var entry in displayed)
            {
                var row=RowGrid();var identity=new StackPanel{Margin=new(10,15,10,15),VerticalAlignment=VerticalAlignment.Center};
                var title=Text(entry.Title,12);title.FontWeight=FontWeights.Medium;title.Margin=new(0,0,0,4);title.ToolTip=entry.Title;identity.Children.Add(title);
                if(isSeries){var episode=Text(entry.Series.SeasonLabel+" · "+entry.Series.EpisodeLabel,11,true);episode.Margin=new(0,0,0,4);identity.Children.Add(episode);}
                var source=Text(entry.Source+(entry.Via==null?"":" · через "+entry.Via),11,true);source.Margin=new(0);identity.Children.Add(source);row.Children.Add(identity);
                row.Children.Add(Cell(entry.Seeds?.ToString()??"—",1));var quality=Cell(entry.Quality,2);quality.FontWeight=FontWeights.SemiBold;row.Children.Add(quality);
                row.Children.Add(Cell(VideoLabel(entry),3,true));row.Children.Add(Cell(entry.Subs,4,true));row.Children.Add(Cell(entry.Voice,5));
                row.Children.Add(Cell(entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"—",6));
                var download=DownloadButton(entry);download.Margin=new(0,8,12,8);Grid.SetColumn(download,7);row.Children.Add(download);
                var rowFrame=new Border{Child=row,BorderThickness=new(0,0,0,1)};rowFrame.SetResourceReference(Border.BorderBrushProperty,"Edge");table.Children.Add(rowFrame);
            }
            var tableFrame=new Border{Child=table,BorderThickness=new(1),CornerRadius=new(13),Margin=new(0,0,0,15)};tableFrame.SetResourceReference(Border.BackgroundProperty,"Panel");tableFrame.SetResourceReference(Border.BorderBrushProperty,"Edge");results.Children.Add(tableFrame);
            MoreResults();
        }
        foreach(var box in filters.Values)box.SelectionChanged+=(_,_)=>{if(!changingFilters){state.VisibleCount=60;Show();}};
        sort.SelectionChanged+=(_,_)=>{if(!changingFilters){state.VisibleCount=60;Show();}};
        toggle.Click+=(_,_)=>{state.More=!state.More;more.Visibility=state.More?Visibility.Visible:Visibility.Collapsed;UpdateActions();};
        reset.Click+=(_,_)=>{changingFilters=true;foreach(var box in filters.Values)box.SelectedIndex=0;sort.SelectedIndex=0;changingFilters=false;state.VisibleCount=60;Show();};
        panel.SizeChanged+=(_,_)=>{var next=Body.ActualWidth<1000;if(next==compact)return;compact=next;Show();};
        Show();
    }
}
