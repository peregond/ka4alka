using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    int QualityMinimum=>prefs.MinimumReleaseHeight==1080?1080:720;
    readonly Dictionary<int,ReleaseCache> qualitySnapshots=[];
    SourceEntry[] KnownQuality(MediaItem item)
    {
        if(releaseViews.TryGetValue(item.Id,out var view))
        {
            if(view.Checking)return [];
            if((view.ReceivedUtc??view.SavedUtc)>DateTime.UtcNow.AddDays(-1)&&liveReleases.TryGetValue(item.Id,out var rows))return rows.ToArray();
        }
        if(qualitySnapshots.TryGetValue(item.Id,out var known)&&known.SavedUtc>DateTime.UtcNow.AddDays(-1))return known.Items;
        var snapshot=catalogIndex.CachedReleaseSnapshot(item);
        var entries=snapshot?.SavedUtc>DateTime.UtcNow.AddDays(-1)?snapshot.Items:[];
        if(qualitySnapshots.Count>=300)qualitySnapshots.Remove(qualitySnapshots.Keys.First());
        qualitySnapshots[item.Id]=new(snapshot?.SavedUtc??DateTime.UtcNow,entries);return entries;
    }
    void ApplyKnownQuality(MediaItem item)=>item.SetReleaseQuality(KnownQuality(item),QualityMinimum);

    FrameworkElement QualityControls(Action changed)
    {
        var controls=new WrapPanel{Margin=new(0,0,0,4),VerticalAlignment=VerticalAlignment.Center};
        var hide=new CheckBox{Content="Скрыть плохое качество",IsChecked=prefs.HidePoorQuality,VerticalAlignment=VerticalAlignment.Center,Margin=new(4,6,12,6),ToolTip="Скрывать экранки и видео ниже выбранного минимума. Неизвестное качество остаётся видимым."};
        AutomationProperties.SetName(hide,"Скрыть плохое качество");controls.Children.Add(hide);
        var caption=Text("Минимум",11,true);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new(0,0,6,0);controls.Children.Add(caption);
        var minimum=new ComboBox{ItemsSource=new[]{"HD Ready","Full HD"},SelectedIndex=QualityMinimum==1080?1:0,Width=112,MinWidth=0,Margin=new(0,0,8,0),ToolTip="HD Ready — от 720p, Full HD — от 1080p. Экранки считаются плохими при любом разрешении."};
        AutomationProperties.SetName(minimum,"Минимальное качество");controls.Children.Add(minimum);
        void Save()
        {
            prefs.HidePoorQuality=hide.IsChecked==true;prefs.MinimumReleaseHeight=minimum.SelectedIndex==1?1080:720;
            try{prefs.Save();}catch(Exception error){Status.Text="Не удалось сохранить фильтр: "+error.Message;}
            changed();
        }
        hide.Checked+=(_,_)=>Save();hide.Unchecked+=(_,_)=>Save();minimum.SelectionChanged+=(_,_)=>Save();
        return controls;
    }
    static TextBlock PoorQualityBadge()=>new(){Text="💩",FontFamily=new FontFamily("Segoe UI Emoji"),FontSize=18,ToolTip="Плохое качество",Margin=new(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};
}
