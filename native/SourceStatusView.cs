using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    sealed class ReleaseView
    {
        public bool Checking,Expanded,Saved;
        public DateTime? SavedUtc,ReceivedUtc;
        public SourceCheck[] Sources=[];
        public CancellationTokenSource? Request;
    }
    readonly Dictionary<int,ReleaseView> releaseViews=[];
    readonly SemaphoreSlim releaseSlots=new(2);
    readonly HashSet<int> deferredDetailRefresh=[];
    int releaseSourceItemId;
    TextBlock? releaseSourceSummary;
    Button? releaseSourceToggle,releaseSourceRetry;
    StackPanel? releaseSourceRows;
    SourceCheck[]? renderedSourceChecks;

    void RetryReleases(MediaItem item)
    {
        if(releaseViews.TryGetValue(item.Id,out var view)&&view.Checking)return;
        onlineIndex.RetryNow();if(DetailMetadataNeedsRetry(item.Id))_ = FetchDetailMetadata(item,true);
        requestedDetails.Remove(item.Id);RefreshDetail(item.Id);
    }
    void RefreshDetail(int id)
    {
        if(closed||activePerson!=null||current?.Id!=id)return;RefreshLoadingIndicator();
        // Keep an open selector stable while providers finish in the background.
        var open=VisualElements<ComboBox>(Body).FirstOrDefault(x=>x.IsDropDownOpen);
        if(open!=null)
        {
            if(deferredDetailRefresh.Add(id))
            {
                void Cleanup(){open.DropDownClosed-=Closed;open.Unloaded-=Unloaded;deferredDetailRefresh.Remove(id);}
                void Closed(object? sender,EventArgs e){Cleanup();RefreshDetail(id);}
                void Unloaded(object sender,RoutedEventArgs e){Cleanup();}
                open.DropDownClosed+=Closed;open.Unloaded+=Unloaded;
            }
            return;
        }
        var owner=VisualElements<Button>(Body).FirstOrDefault(x=>x.ContextMenu?.IsOpen==true);
        if(owner?.ContextMenu is { } menu)
        {
            if(deferredDetailRefresh.Add(id))
            {
                void Cleanup(){menu.Closed-=Closed;owner.Unloaded-=Unloaded;deferredDetailRefresh.Remove(id);}
                void Closed(object sender,RoutedEventArgs e){Cleanup();RefreshDetail(id);}
                void Unloaded(object sender,RoutedEventArgs e){Cleanup();}
                menu.Closed+=Closed;owner.Unloaded+=Unloaded;
            }
            return;
        }
        var scroller=FindVisual<ScrollViewer>(Body,_=>true);var offset=scroller?.VerticalOffset??0;
        Render();UpdateLayout();FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToVerticalOffset(offset);
    }
    void RenderSourceStatus(WrapPanel toolbar,StackPanel target,MediaItem item)
    {
        if(!releaseViews.ContainsKey(item.Id))releaseViews[item.Id]=new();
        releaseSourceItemId=item.Id;renderedSourceChecks=null;
        var show=Button("Источники",()=>{});show.Style=(Style)FindResource("QuietButton");show.Padding=new(8,6,8,6);show.MinHeight=30;show.Margin=new(0,0,4,0);show.BorderThickness=new(0);show.VerticalAlignment=VerticalAlignment.Center;
        show.ToolTip="Состояние трекеров и онлайн-индекса";AutomationProperties.SetName(show,"Показать состояние источников");toolbar.Children.Add(show);releaseSourceToggle=show;
        var retry=ActionButton("Обновить","IconRefresh",()=>RetryReleases(item));retry.MinHeight=30;retry.Padding=new(8,6,8,6);retry.Margin=new(0,0,6,0);retry.BorderThickness=new(0);retry.VerticalAlignment=VerticalAlignment.Center;
        retry.ToolTip="Повторить поиск раздач во всех источниках";AutomationProperties.SetName(retry,"Обновить варианты загрузки");toolbar.Children.Add(retry);releaseSourceRetry=retry;
        var summary=Text("",11,true);summary.Name="SourceSummary";summary.Margin=new(4,6,4,6);summary.MaxWidth=260;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.TextWrapping=TextWrapping.NoWrap;summary.VerticalAlignment=VerticalAlignment.Center;
        toolbar.Children.Add(summary);releaseSourceSummary=summary;
        var rows=new StackPanel{Name="ReleaseSourceDetails",Margin=new(4,0,0,12),Visibility=Visibility.Collapsed};AutomationProperties.SetName(rows,"Состояние источников раздач");target.Children.Add(rows);releaseSourceRows=rows;
        show.Click+=(_,_)=>{if(!releaseViews.TryGetValue(item.Id,out var view))return;view.Expanded=!view.Expanded;RefreshReleaseSourceStatus();};
        RefreshReleaseSourceStatus();
    }
    void RefreshReleaseSourceStatus()
    {
        if(current?.Id!=releaseSourceItemId||releaseSourceSummary==null||releaseSourceRows==null||releaseSourceToggle==null||releaseSourceRetry==null||!releaseViews.TryGetValue(releaseSourceItemId,out var view))return;
        var failures=view.Sources.Count(x=>x.State is SourceState.TimedOut or SourceState.Unavailable);
        var summary=view.Checking?(liveReleases.GetValueOrDefault(releaseSourceItemId)?.Count>0?"Найденные варианты доступны":""):
            failures>0?$"Не ответили: {failures}"+(view.Saved?" · есть сохранённые":""):view.Saved?"Есть сохранённые варианты":"";
        releaseSourceSummary.Text=summary;releaseSourceSummary.ToolTip=summary;releaseSourceSummary.Visibility=summary.Length>0?Visibility.Visible:Visibility.Collapsed;
        releaseSourceRetry.IsEnabled=!view.Checking;
        releaseSourceToggle.Content=(view.Expanded?"Скрыть источники":"Источники")+(view.Sources.Length>0?$" · {view.Sources.Length}":"");
        AutomationProperties.SetItemStatus(releaseSourceToggle,view.Checking?"Проверяем источники":failures>0?$"Не ответили: {failures}":view.Sources.Length>0?"Проверка завершена":"Источники ещё не проверяли");
        releaseSourceRows.Visibility=view.Expanded?Visibility.Visible:Visibility.Collapsed;
        if(ReferenceEquals(renderedSourceChecks,view.Sources)&&view.Sources.Length>0)return;
        renderedSourceChecks=view.Sources;releaseSourceRows.Children.Clear();
        if(view.Sources.Length==0)
        {
            releaseSourceRows.Children.Add(Text(view.Checking?"Проверяем доступные источники…":"Обнови поиск, чтобы проверить источники.",12,true));return;
        }
        foreach(var source in view.Sources)
        {
            var row=new Grid{Margin=new(0,0,0,8)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1.6,GridUnitType.Star)});
            var name=Text(source.Name,12);name.FontWeight=FontWeights.Medium;name.Margin=new(0,0,12,0);row.Children.Add(name);
            var state=source.State switch
            {
                SourceState.Searching=>"Проверяем…",SourceState.Ready=>$"Найдено вариантов: {source.Count}",SourceState.Empty=>"Подходящих вариантов не найдено",
                SourceState.TimedOut=>"Не ответил вовремя",SourceState.Unavailable=>"Временно недоступен",SourceState.Indexed=>$"В индексе: {source.Count} · трекер отдельно не проверялся",SourceState.Saved=>$"Сохранено вариантов: {source.Count}",_=>""
            };
            var details=Text(state,12,true);details.Margin=new(0);Grid.SetColumn(details,1);row.Children.Add(details);releaseSourceRows.Children.Add(row);
        }
    }
}
