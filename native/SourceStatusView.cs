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

    void RetryReleases(MediaItem item)
    {
        if(releaseViews.TryGetValue(item.Id,out var view)&&view.Checking)return;
        onlineIndex.RetryNow();requestedDetails.Remove(item.Id);RefreshDetail(item.Id);
    }
    void RefreshDetail(int id)
    {
        if(closed||current?.Id!=id)return;RefreshLoadingIndicator();
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
        var scroller=FindVisual<ScrollViewer>(Body,_=>true);var offset=scroller?.VerticalOffset??0;
        Render();UpdateLayout();FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToVerticalOffset(offset);
    }
    static string DateLabel(DateTime utc)=>utc.ToLocalTime().ToString(utc.ToLocalTime().Date==DateTime.Today?"'сегодня в' HH:mm":"dd.MM.yyyy HH:mm");
    void RenderSourceStatus(StackPanel target,MediaItem item)
    {
        if(!releaseViews.TryGetValue(item.Id,out var view))return;
        var content=new StackPanel();
        var summary=view.Checking?"Проверяем источники. Найденные варианты уже можно скачивать.":view.Saved?"Есть сохранённые варианты. Число отдающих могло измениться.":view.ReceivedUtc.HasValue?"Варианты получены "+DateLabel(view.ReceivedUtc.Value)+".":"Свежих вариантов пока нет.";
        var text=Text(summary,12,true);text.Name="SourceSummary";text.Margin=new(0,0,0,8);content.Children.Add(text);
        if(view.Saved&&view.SavedUtc.HasValue){var date=Text("Сохранённая подборка: "+DateLabel(view.SavedUtc.Value),11,true);date.Margin=new(0,0,0,8);content.Children.Add(date);}
        var actions=new WrapPanel();content.Children.Add(actions);
        var show=Button((view.Expanded?"Скрыть источники":"Источники")+$" · {view.Sources.Length}",()=>{});show.Style=(Style)FindResource("QuietButton");show.Padding=new(0,5,12,5);show.MinHeight=28;show.Margin=new(0,0,8,0);AutomationProperties.SetName(show,"Показать состояние источников");actions.Children.Add(show);
        var retry=ActionButton(view.Checking?"Проверяем…":"Обновить","IconRefresh",()=>RetryReleases(item));retry.IsEnabled=!view.Checking;retry.MinHeight=28;retry.Padding=new(8,5,8,5);retry.Margin=new(0);AutomationProperties.SetName(retry,"Обновить варианты загрузки");actions.Children.Add(retry);
        var rows=new StackPanel{Visibility=view.Expanded?Visibility.Visible:Visibility.Collapsed,Margin=new(0,12,0,0)};content.Children.Add(rows);
        show.Click+=(_,_)=>{view.Expanded=!view.Expanded;rows.Visibility=view.Expanded?Visibility.Visible:Visibility.Collapsed;show.Content=(view.Expanded?"Скрыть источники":"Источники")+$" · {view.Sources.Length}";};
        foreach(var source in view.Sources)
        {
            var row=new StackPanel{Margin=new(0,0,0,10)};var name=Text(source.Name,12);name.FontWeight=FontWeights.SemiBold;name.Margin=new(0,0,0,3);row.Children.Add(name);
            var state=source.State switch
            {
                SourceState.Searching=>"Проверяем…",SourceState.Ready=>$"Найдено вариантов: {source.Count}",SourceState.Empty=>"Подходящих вариантов не найдено",
                SourceState.TimedOut=>"Не ответил вовремя",SourceState.Unavailable=>"Временно недоступен",SourceState.Indexed=>$"В индексе: {source.Count} · трекер отдельно не проверялся",SourceState.Saved=>$"Сохранено вариантов: {source.Count}",_=>""
            };
            if(source.LastSuccessUtc.HasValue)state+=" · последний ответ "+DateLabel(source.LastSuccessUtc.Value);
            var details=Text(state,11,true);details.Margin=new(0);row.Children.Add(details);rows.Children.Add(row);
        }
        var frame=new Border{Child=content,Padding=new(16),CornerRadius=new(14),BorderThickness=new(1),Margin=new(0,0,0,16)};
        frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"Edge");target.Children.Add(frame);
    }
}
