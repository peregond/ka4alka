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
        qualitySnapshots.TryGetValue(item.Id,out var known);
        if(releaseViews.TryGetValue(item.Id,out var view)&&
           (view.ReceivedUtc??view.SavedUtc)>DateTime.UtcNow.AddDays(-7)&&
           (known==null||(view.ReceivedUtc??view.SavedUtc)>=known.SavedUtc)&&liveReleases.TryGetValue(item.Id,out var rows))return rows.ToArray();
        if(known?.SavedUtc>DateTime.UtcNow.AddDays(-7))return known.Items;
        var snapshot=catalogIndex.CachedReleaseSnapshot(item);
        var entries=snapshot?.Items??[];
        if(qualitySnapshots.Count>=300)qualitySnapshots.Remove(qualitySnapshots.Keys.First());
        qualitySnapshots[item.Id]=new(snapshot?.SavedUtc??DateTime.UtcNow,entries,snapshot?.Sources);return entries;
    }
    void ApplyKnownQuality(MediaItem item)=>item.SetReleaseQuality(KnownQuality(item),720);
    bool CatalogQualityMatches(MediaItem item)
    {
        ApplyKnownQuality(item);
        return (!prefs.HidePoorQuality||!item.OnlyPoorQuality)&&
            (prefs.CatalogQualityHeight==0||item.HasReleaseResolution(prefs.CatalogQualityHeight));
    }

    readonly SemaphoreSlim catalogQualitySlots=new(2,2);
    readonly Dictionary<int,DateTime> catalogQualityAttempts=[];
    CancellationTokenSource? catalogQualityRequest;
    string catalogQualityCheckKey="";
    sealed class CatalogQualityQueue(bool force)
    {
        public readonly Queue<MediaItem> Pending=[];
        public readonly HashSet<int> Scheduled=[];
        public readonly bool Force=force;
        public int FallbackBudget=4;
    }
    CatalogQualityQueue? catalogQualityQueue;
    Func<MediaItem,CancellationToken,Task<IReadOnlyList<SourceEntry>>>? catalogQualityProvider;
    bool forceCatalogQualityCheck;
    bool CatalogQualityChecking=>catalogQualityRequest is {IsCancellationRequested:false};
    void CancelCatalogQualityCheck()
    {
        catalogQualityRequest?.Cancel();catalogQualityRequest=null;catalogQualityCheckKey="";catalogQualityQueue=null;
    }
    void ResetCatalogQualityChecks()
    {
        CancelCatalogQualityCheck();catalogQualityAttempts.Clear();qualitySnapshots.Clear();forceCatalogQualityCheck=true;
    }
    bool FreshQuality(MediaItem item)
    {
        var now=DateTime.UtcNow;
        if(releaseViews.TryGetValue(item.Id,out var view)&&(view.ReceivedUtc??view.SavedUtc)>now.AddDays(-1))return true;
        if(qualitySnapshots.TryGetValue(item.Id,out var memory)&&memory.SavedUtc>now.AddDays(-1)&&memory.Sources is {Length:>0}&&memory.Sources.Any(x=>x.State is SourceState.Ready or SourceState.Empty))return true;
        return catalogIndex.CachedReleaseSnapshot(item)?.SavedUtc>now.AddDays(-1);
    }
    void StartCatalogQualityCheck(IReadOnlyList<MediaItem> currentPage)
    {
        if(prefs.CatalogQualityHeight==0||closed||current!=null||activePerson!=null){CancelCatalogQualityCheck();return;}
        // Main-page identity defines the request lifetime. Late discovery rows
        // join that same bounded queue, so progress does not cancel its work.
        var page=currentPage.Where(x=>x.IsLive).DistinctBy(x=>x.Id).Take(CatalogPaging.Size).ToArray();
        var key=CurrentCatalogKey+"|"+favoritesOnly+"|"+prefs.CatalogQualityHeight+"|"+string.Join(',',page.Select(x=>x.Id));
        if(catalogQualityCheckKey!=key){CancelCatalogQualityCheck();catalogQualityCheckKey=key;}
        var items=DiscoveryQualityCandidates(page);
        if(items.Length==0||catalogQualityProvider==null&&Environment.GetCommandLineArgs().Any(x=>x.EndsWith("-smoke-test",StringComparison.Ordinal)))return;
        var work=catalogQualityQueue??=new CatalogQualityQueue(forceCatalogQualityCheck);forceCatalogQualityCheck=false;
        foreach(var item in items)
            if(work.Scheduled.Count<CatalogPaging.Size*4&&work.Scheduled.Add(item.Id)&&(work.Force||!FreshQuality(item)&&(!catalogQualityAttempts.TryGetValue(item.Id,out var attempted)||attempted<DateTime.UtcNow.AddMinutes(-5))))work.Pending.Enqueue(item);
        if(catalogQualityRequest!=null||work.Pending.Count==0)return;
        var request=catalogQualityRequest=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _ = CheckCatalogQuality(work,prefs.CatalogQualityHeight,request,key);
    }
    async Task CheckCatalogQuality(CatalogQualityQueue work,int height,CancellationTokenSource request,string key)
    {
        var token=request.Token;
        bool IsCurrent()=>!closed&&!token.IsCancellationRequested&&ReferenceEquals(catalogQualityRequest,request)&&catalogQualityCheckKey==key&&current==null&&activePerson==null;
        async Task Check(MediaItem item)
        {
            await catalogQualitySlots.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if(!work.Force&&FreshQuality(item))return;
                var saved=KnownQuality(item);var indexed=Array.Empty<SourceEntry>();
                SourceCheck indexState;
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        indexed=(await (catalogQualityProvider==null?onlineIndex.Releases(item,deadline.Token):catalogQualityProvider(item,deadline.Token)).WaitAsync(deadline.Token)).ToArray();
                        var now=DateTime.UtcNow;indexState=new("Онлайн-индекс",indexed.Length>0?SourceState.Ready:SourceState.Empty,indexed.Length,now,now);
                    }
                    catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                    catch(OperationCanceledException){indexState=new("Онлайн-индекс",SourceState.TimedOut,CheckedUtc:DateTime.UtcNow);}
                    catch{indexState=new("Онлайн-индекс",SourceState.Unavailable,CheckedUtc:DateTime.UtcNow);}
                }
                token.ThrowIfCancellationRequested();
                var fresh=indexed;var sources=new[]{indexState};
                if(!ReleaseQuality.HasResolution(ReleaseSearch.WithSaved(indexed,saved),height)&&Interlocked.Decrement(ref work.FallbackBudget)>=0)
                {
                    // A small fallback budget avoids a full multi-tracker source
                    // scan for every poster, and does not fetch movie metadata.
                    var providers=NativeReleaseSources.Create(sourceClient,item).Where(x=>x.Name is "RuTor" or "RuTracker · через Knaben");
                    var fallback=await ReleaseSearch.RunAsync(providers,ct:token,timeout:TimeSpan.FromSeconds(10));
                    fresh=ReleaseSearch.WithSaved(indexed,fallback.Items);sources=sources.Concat(fallback.Sources).ToArray();
                }
                token.ThrowIfCancellationRequested();
                var rows=ReleaseSearch.WithSaved(fresh,saved);
                // A limited quality lookup cannot prove that every download
                // source is empty, or make saved links look newly verified.
                if(fresh.Length>0)await catalogIndex.CacheReleasesAsync(item,rows,sources,token);
                if(!IsCurrent())return;
                var nowChecked=DateTime.UtcNow;
                if(catalogQualityAttempts.Count>=300)catalogQualityAttempts.Remove(catalogQualityAttempts.Keys.First());
                catalogQualityAttempts[item.Id]=nowChecked;
                var evidenceTime=fresh.Length>0||sources.Any(x=>x.State is SourceState.Ready or SourceState.Empty)?nowChecked:
                    qualitySnapshots.GetValueOrDefault(item.Id)?.SavedUtc??DateTime.MinValue;
                qualitySnapshots[item.Id]=new(evidenceTime,rows,sources);
                ApplyKnownQuality(item);
                if(!VisualElements<Button>(RootGrid).Any(x=>x.ContextMenu?.IsOpen==true))RenderCatalogKeepingPosition();
            }
            finally{catalogQualitySlots.Release();}
        }
        async Task Worker()
        {
            while(IsCurrent()&&work.Pending.Count>0)
            {
                var item=work.Pending.Dequeue();
                try{await Check(item);}
                catch(System.IO.IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        try{await Task.Yield();await Task.WhenAll(Worker(),Worker());}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(System.IO.IOException){}
        catch(UnauthorizedAccessException){}
        catch(Exception error){ErrorLog.Write(error);}
        finally
        {
            var active=ReferenceEquals(catalogQualityRequest,request);
            if(token.IsCancellationRequested)work.Pending.Clear();
            if(active)catalogQualityRequest=null;
            request.Dispose();
            if(active&&!closed&&current==null&&activePerson==null&&(section is "Фильмы" or "Сериалы")&&!VisualElements<Button>(RootGrid).Any(x=>x.ContextMenu?.IsOpen==true))RenderCatalogKeepingPosition();
        }
    }

    FrameworkElement CatalogQualityControls(Action changed)
    {
        void Save(bool hide,int height)
        {
            var oldHide=prefs.HidePoorQuality;var oldHeight=prefs.CatalogQualityHeight;
            prefs.HidePoorQuality=hide;prefs.CatalogQualityHeight=ReleaseQuality.CatalogHeight(height);
            try{prefs.Save();}catch(Exception error){prefs.HidePoorQuality=oldHide;prefs.CatalogQualityHeight=oldHeight;Status.Text="Не удалось сохранить фильтр: "+error.Message;return;}
            CancelCatalogQualityCheck();changed();
        }
        var label=prefs.CatalogQualityHeight switch{2160=>"Качество · 4K",1080=>"Качество · FullHD",720=>"Качество · HD Ready",_=>"Качество"};
        var button=Button("",()=>{});button.Style=(Style)FindResource("PillButton");button.Content=ChipContent(label);
        StyleChip(button,prefs.CatalogQualityHeight!=0);
        button.ToolTip="Показывать фильмы и сериалы с раздачей выбранного разрешения. Экранки не считаются HD или 4K.";
        AutomationProperties.SetName(button,"Качество каталога");
        AutomationProperties.SetItemStatus(button,(prefs.HidePoorQuality?"Плохое качество скрыто. ":"")+(prefs.CatalogQualityHeight==0?"Любое разрешение":label));
        var menu=new ToggleContextMenu{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        menu.SetResourceReference(Control.BackgroundProperty,"Raised");menu.SetResourceReference(Control.ForegroundProperty,"Text");
        var hide=new MenuItem{Header="Скрывать плохое качество",Tag="hide-poor",IsCheckable=true,IsChecked=prefs.HidePoorQuality};
        AutomationProperties.SetName(hide,"Скрывать плохое качество");
        hide.Click+=(_,_)=>{menu.IsOpen=false;Save(!prefs.HidePoorQuality,prefs.CatalogQualityHeight);};menu.Items.Add(hide);menu.Items.Add(new Separator());
        foreach(var choice in new[]{(Height:0,Label:"Любое качество"),(Height:2160,Label:"4K"),(Height:1080,Label:"FullHD"),(Height:720,Label:"HD Ready")})
        {
            var option=new MenuItem{Header=choice.Label,Tag=choice.Height,IsCheckable=true,IsChecked=prefs.CatalogQualityHeight==choice.Height};
            option.ToolTip=choice.Height==0?"Снять ограничение по разрешению":"Есть доступная раздача "+choice.Height+"p";
            option.Click+=(_,_)=>{menu.IsOpen=false;Save(prefs.HidePoorQuality,prefs.CatalogQualityHeight==choice.Height?0:choice.Height);};menu.Items.Add(option);
        }
        AttachMenuToggle(button,menu);return button;
    }

    FrameworkElement QualityControls(Action changed)
    {
        var controls=new WrapPanel{Margin=new(0,0,0,4),VerticalAlignment=VerticalAlignment.Center};
        var hide=new CheckBox{Content="Скрыть плохое качество",IsChecked=prefs.HidePoorQuality,VerticalAlignment=VerticalAlignment.Center,Margin=new(4,6,12,6),ToolTip="Скрывать экранки и видео ниже выбранного минимума. Неизвестное качество остаётся видимым."};
        hide.SetResourceReference(Control.ForegroundProperty,"Text");
        AutomationProperties.SetName(hide,"Скрыть плохое качество");controls.Children.Add(hide);
        var caption=Text("Качество",11,true);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new(0,0,6,0);controls.Children.Add(caption);
        var minimum=new ComboBox{ItemsSource=new[]{"HD Ready","Full HD"},SelectedIndex=QualityMinimum==1080?1:0,Width=112,MinWidth=0,Margin=new(0,0,8,0),ToolTip="HD Ready — от 720p, Full HD — от 1080p. Экранки считаются плохими при любом разрешении."};
        AutomationProperties.SetName(minimum,"Качество раздач");controls.Children.Add(minimum);
        void Save()
        {
            prefs.HidePoorQuality=hide.IsChecked==true;prefs.MinimumReleaseHeight=minimum.SelectedIndex==1?1080:720;
            try{prefs.Save();}catch(Exception error){Status.Text="Не удалось сохранить фильтр: "+error.Message;}
            changed();
        }
        hide.Checked+=(_,_)=>Save();hide.Unchecked+=(_,_)=>Save();minimum.SelectionChanged+=(_,_)=>Save();
        return controls;
    }
    static TextBlock PoorQualityBadge(string label)=>new(){Text=label,FontSize=11,FontWeight=FontWeights.Medium,ToolTip=label=="Экранка"?"Экранная запись: плохое качество изображения":"Видео ниже выбранного минимального качества",Margin=new(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};
}
