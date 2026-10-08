using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace Kachalka;

public partial class MainWindow
{
    void RenderCinemaConnections(StackPanel panel,MediaItem item,Panel? participantsHost=null)
    {
        var people=new StackPanel();
        people.Children.Add(Text("Актёры и съёмочная группа",18));
        if(item.People.Length==0)
        {
            people.Children.Add(Text(DetailMetadataLoading(item.Id)?"Загружаем участников…":"Участники пока недоступны у источника.",12,true));
            if(DetailMetadataNeedsRetry(item.Id)){var retry=ActionButton("Загрузить участников","IconRefresh",()=>RetryDetailMetadata(item));retry.HorizontalAlignment=HorizontalAlignment.Left;people.Children.Add(retry);}
        }
        foreach(var group in item.People.GroupBy(x=>x.Role))
        {
            var role=Text(group.Key,12,true);role.Margin=new(0,12,0,10);people.Children.Add(role);
            var row=new UniformGrid{Name="CinemaPeopleGrid",Columns=3,Margin=new(0,0,-8,0)};people.Children.Add(row);
            row.SizeChanged+=(_,_)=>{var columns=row.ActualWidth>=366?3:row.ActualWidth>=236?2:1;if(row.Columns!=columns)row.Columns=columns;};
            foreach(var person in group)
            {
                var button=Button(person.Name,()=>OpenPerson(person,item));button.Style=(Style)FindResource("PillButton");
                var tile=new StackPanel();tile.Children.Add(PersonPortrait(person,96,120,item));var name=Text(person.Name,12);name.TextAlignment=TextAlignment.Center;name.MinHeight=32;name.MaxHeight=36;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new(0);tile.Children.Add(name);button.Content=tile;
                button.Padding=new(8);button.Margin=new(0,0,8,10);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;button.ToolTip=person.Name;
                button.SetResourceReference(Control.BackgroundProperty,"PanelAlt");button.SetResourceReference(Control.BorderBrushProperty,"EdgeSoft");
                AutomationProperties.SetName(button,"Открыть карточку: "+person.Name+", "+person.Role);row.Children.Add(button);
            }
        }
        var participants=new Border{Name="CinemaParticipants",Child=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=people,MaxHeight=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},CornerRadius=new(22),Padding=new(20),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Top};
        participants.SetResourceReference(Border.BackgroundProperty,"Panel");participants.SetResourceReference(Border.BorderBrushProperty,"Edge");
        if(participantsHost!=null)participantsHost.Children.Add(participants);else panel.Children.Add(participants);
        if(item.Awards.Length>0)
        {
            panel.Children.Add(Text("Награды и номинации",18));
            foreach(var award in item.Awards)panel.Children.Add(Text(award.Label,12,true));
        }
        if(item.Collections.Length>0)
        {
            panel.Children.Add(Text("Франшизы и подборки",18));var row=new WrapPanel{Margin=new(0,0,0,20)};panel.Children.Add(row);
            foreach(var collection in item.Collections)
            {
                var button=Button(collection.Name+" · "+collection.Kind,()=>OpenCollection(collection));button.Style=(Style)FindResource("PillButton");row.Children.Add(button);
            }
        }
    }
    (Window Dialog,StackPanel Body) CinemaDialog(string title,string subtitle)
    {
        var body=new StackPanel{Margin=new(22)};
        var dialog=new Window{Owner=this,Title=title,Width=680,Height=720,MinWidth=360,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        dialog.PreviewKeyDown+=(_,eventArgs)=>{if(eventArgs.Key==System.Windows.Input.Key.Escape){dialog.Close();eventArgs.Handled=true;}};
        dialog.SetResourceReference(Window.BackgroundProperty,"Panel");dialog.SetResourceReference(Window.ForegroundProperty,"Text");
        var back=Button("← Назад к фильму",dialog.Close);back.Style=(Style)FindResource("QuietButton");back.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(back);
        body.Children.Add(Text(title,26));body.Children.Add(Text(subtitle,12,true));return(dialog,body);
    }
    void CinemaFilms(StackPanel body,string title,IEnumerable<MediaItem> items,Window dialog)
    {
        var films=items.ToArray();body.Children.Add(Text(title,18));
        if(films.Length==0){body.Children.Add(Text("Данные пока недоступны.",12,true));return;}
        foreach(var film in films)
        {
            var button=Button(film.Title+" · "+(film.Year>0?film.Year.ToString():"Год неизвестен")+(CinemaMetadata.Rating(film)>0?" · КП "+film.Kinopoisk:""),()=>{dialog.Close();current=film;section=film.Section;Render();});
            button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;
            button.Content=new TextBlock{Text=button.Content.ToString(),TextWrapping=TextWrapping.Wrap};
            AutomationProperties.SetName(button,"Открыть "+film.Title);body.Children.Add(button);
        }
    }
    CinemaPerson? activePerson;
    MediaItem? personOrigin;
    string? personSection;
    CancellationTokenSource? personRequest;
    (CinemaPerson Person,MediaItem Origin)? returnPerson;
    void OpenPerson(CinemaPerson person,MediaItem origin)
    {
        activePerson=person;personOrigin=origin;section=origin.Section;personSection=section;current=origin;Render();
    }
    void CinemaBack()
    {
        if(returnPerson is {} previous){returnPerson=null;OpenPerson(previous.Person,previous.Origin);return;}
        current=null;Render();
    }
    Border PersonPortrait(CinemaPerson person,double width,double height,MediaItem? origin=null)
    {
        var grid=new Grid();var fallback=Text(string.Join("",person.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x=>x[0])),24,true);
        fallback.HorizontalAlignment=HorizontalAlignment.Center;fallback.VerticalAlignment=VerticalAlignment.Center;fallback.Margin=new(0);grid.Children.Add(fallback);
        var image=new Image{Stretch=Stretch.UniformToFill};AutomationProperties.SetName(image,"Фотография: "+person.Name);grid.Children.Add(image);
        var progress=Text("Фото…",9,true);progress.HorizontalAlignment=HorizontalAlignment.Center;progress.VerticalAlignment=VerticalAlignment.Bottom;progress.Margin=new(0,0,0,7);progress.Visibility=Visibility.Collapsed;progress.IsHitTestVisible=false;grid.Children.Add(progress);
        var frame=new Border{Width=width,Height=height,CornerRadius=new(11),ClipToBounds=true,Child=grid,Margin=new(0,0,0,8)};frame.SetResourceReference(Border.BackgroundProperty,"Selected");frame.SizeChanged+=(_,_)=>ClipPoster(frame);
        CancellationTokenSource? request=null;bool attempted=false;string? portraitPath=null;
        var scrollers=new List<ScrollViewer>();
        bool InViewport()
        {
            if(!frame.IsLoaded||frame.ActualWidth<=0||frame.ActualHeight<=0)return false;
            try
            {
                for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollViewer scroll&&!frame.TransformToAncestor(scroll).TransformBounds(new Rect(new Point(),frame.RenderSize)).IntersectsWith(new Rect(0,0,scroll.ActualWidth,scroll.ActualHeight)))return false;
                return true;
            }
            catch(InvalidOperationException){return false;}
        }
        Button retry=null!;retry=Button("↻",()=>{_ = LoadPhoto(true);});retry.Style=(Style)FindResource("QuietButton");retry.Width=26;retry.Height=26;retry.MinHeight=26;retry.Padding=new(3);retry.Margin=new(4);retry.HorizontalAlignment=HorizontalAlignment.Right;retry.VerticalAlignment=VerticalAlignment.Bottom;retry.Visibility=Visibility.Collapsed;retry.ToolTip="Повторить загрузку фотографии";
        retry.Click+=(_,args)=>args.Handled=true;AutomationProperties.SetName(retry,"Повторить фотографию: "+person.Name);grid.Children.Add(retry);
        async Task LoadPhoto(bool force=false)
        {
            if(request!=null||!InViewport()||image.Source!=null||attempted&&!force)return;
            attempted=true;using var pending=new CancellationTokenSource();request=pending;retry.Visibility=Visibility.Collapsed;progress.Visibility=Visibility.Visible;frame.ToolTip="Загружаем фотографию…";
            try
            {
                var portrait=await new CinemaPeople(sourceClient).ResolvePortrait(person,pending.Token,origin);
                var url=portrait.Url;
                if(url==null)
                {
                    var unavailable=portrait.Status==PortraitStatus.Unavailable;
                    retry.Visibility=unavailable?Visibility.Visible:Visibility.Collapsed;
                    frame.ToolTip=unavailable?"Не удалось загрузить фотографию. Можно повторить.":"У источника пока нет фотографии.";return;
                }
                var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));portraitPath=Path.Combine(Preferences.DataDir,"portraits",key+".img");
                await coverSlots.WaitAsync(pending.Token);
                try
                {
                    using var download=CancellationTokenSource.CreateLinkedTokenSource(pending.Token);download.CancelAfter(TimeSpan.FromSeconds(12));
                    var (bitmap,_)=await CoverCache.Load(portraitPath,2*1024*1024,
                        ct=>sourceClient.Read(new Uri(url),2*1024*1024,ct),bytes=>{using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=(int)Math.Ceiling(width*2);result.StreamSource=stream;result.EndInit();result.Freeze();return result;},download.Token);
                    if(image.IsLoaded&&!pending.IsCancellationRequested){image.Source=bitmap;fallback.Visibility=Visibility.Collapsed;frame.ToolTip=person.Name;}
                }
                finally{coverSlots.Release();}
            }
            catch(OperationCanceledException)when(pending.IsCancellationRequested){}
            catch{if(image.IsLoaded&&!pending.IsCancellationRequested){retry.Visibility=Visibility.Visible;frame.ToolTip="Не удалось загрузить фотографию. Можно повторить.";}}
            finally
            {
                progress.Visibility=Visibility.Collapsed;if(ReferenceEquals(request,pending))request=null;
                if(pending.IsCancellationRequested&&image.Source==null){attempted=false;if(image.IsLoaded)_=Dispatcher.BeginInvoke(new Action(VisiblePhoto));}
            }
        }
        void VisiblePhoto(){if(portraitPath!=null&&InViewport())CacheFiles.Touch(portraitPath);_ = LoadPhoto();}
        void Scrolled(object sender,ScrollChangedEventArgs args)=>VisiblePhoto();
        void ViewportResized(object sender,SizeChangedEventArgs args)=>VisiblePhoto();
        image.Loaded+=(_,_)=>
        {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll){scrollers.Add(scroll);scroll.ScrollChanged+=Scrolled;scroll.SizeChanged+=ViewportResized;}
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(VisiblePhoto));
        };
        image.SizeChanged+=(_,_)=>VisiblePhoto();
        image.Unloaded+=(_,_)=>{request?.Cancel();foreach(var scroll in scrollers){scroll.ScrollChanged-=Scrolled;scroll.SizeChanged-=ViewportResized;}scrollers.Clear();};
        return frame;
    }
    void RenderPerson(CinemaPerson person,MediaItem origin)
    {
        PageHeader.Children.Add(ActionButton("Назад к фильму","IconBack",()=>{activePerson=null;current=personOrigin??origin;Render();},"QuietButton"));
        var body=new StackPanel{Margin=new(0,0,10,0)};
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var hero=new WrapPanel();hero.Children.Add(PersonPortrait(person,150,190,origin));
        var identity=new StackPanel{Margin=new(20,0,0,0),MaxWidth=450};identity.Children.Add(Text(person.Role,12,true));identity.Children.Add(Text(person.Name,30));identity.Children.Add(Text("Биография и фильмы участника",13,true));hero.Children.Add(identity);
        var frame=new Border{Child=hero,CornerRadius=new(22),Padding=new(20),Margin=new(0,0,0,24),BorderThickness=new(1)};frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"Edge");body.Children.Add(frame);
        var content=new StackPanel();body.Children.Add(content);var request=personRequest=new CancellationTokenSource();
        async Task Refresh()
        {
            content.Children.Clear();content.Children.Add(Text("Загружаем биографию и фильмографию…",13,true));
            try
            {
                var profile=await new LiveCatalog(sourceClient).Person(person,request.Token,origin,liveItems.Concat(prefs.LiveFavorites));
                if(request.IsCancellationRequested)return;
                content.Children.Clear();content.Children.Add(Text("Биография",18));content.Children.Add(Text(profile.Description.Length>0?profile.Description:"Биография пока недоступна.",13,true));
                var sourceUrl=profile.SourceUrl??"https://ru.wikipedia.org/wiki/"+Uri.EscapeDataString(person.Name.Replace(' ','_'));
                var source=Button("Википедия · биография и фотографии",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourceUrl){UseShellExecute=true}));source.Style=(Style)FindResource("QuietButton");content.Children.Add(source);
                void Films(string title,IEnumerable<MediaItem> items)
                {
                    content.Children.Add(Text(title,18));var films=items.ToArray();if(films.Length==0){content.Children.Add(Text("Данные пока недоступны.",12,true));return;}
                    foreach(var film in films)
                    {
                        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(64)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                        var poster=new Image{Width=48,Height=72,DataContext=film,Stretch=Stretch.Uniform};poster.Loaded+=SourceCover;row.Children.Add(poster);
                        var label=Text(film.Title+" · "+film.Year+(CinemaMetadata.Rating(film)>0?" · КП "+film.Kinopoisk:""),14);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
                        var button=Button(film.Title,()=>{returnPerson=(person,personOrigin??origin);activePerson=null;current=film;section=film.Section;Render();});button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.HorizontalAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(button,"Открыть "+film.Title);content.Children.Add(button);
                    }
                }
                Films("Выбор зрителей · топ по Кинопоиску",CinemaMetadata.Top(profile.Filmography));content.Children.Add(Text("Рейтинг Кинопоиска среди фильмов, предоставленных источником.",11,true));
                var known=profile.Filmography.Select(x=>x.Id).ToHashSet();
                Films("Фильмы с наградами за работу этого участника",liveItems.Concat(prefs.LiveFavorites).Append(origin).Where(x=>known.Contains(x.Id)&&x.Awards.Any(a=>a.Winner&&person.PageUrl.Length>0&&a.PersonUrl==person.PageUrl)).DistinctBy(x=>x.Id));
                Films("Фильмография",profile.Filmography.OrderByDescending(x=>x.Year));
            }
            catch(OperationCanceledException){}
            catch{if(!request.IsCancellationRequested){content.Children.Clear();content.Children.Add(Text("Не удалось загрузить карточку человека.",13,true));content.Children.Add(Button("Повторить",()=>{_=Refresh();}));}}
        }
        _=Refresh();
    }
    void OpenCollection(CinemaCollection collection)
    {
        var (dialog,body)=CinemaDialog(collection.Name,collection.Kind);using var request=new CancellationTokenSource();dialog.Closed+=(_,_)=>request.Cancel();
        dialog.Loaded+=async(_,_)=>
        {
            var status=Text("Загружаем подборку…",13,true);body.Children.Add(status);
            try
            {
                var url=CinemaMetadata.CatalogUrl(collection.PageUrl,"/collections/","/franchise/")??throw new ArgumentException("Неверный адрес подборки.");
                var bytes=await sourceClient.Read(new Uri(url),4*1024*1024,request.Token);if(request.IsCancellationRequested)return;
                body.Children.Remove(status);CinemaFilms(body,"Фильмы и сериалы",LiveCatalog.Parse(bytes,"Фильмы").Concat(LiveCatalog.Parse(bytes,"Сериалы")),dialog);
            }
            catch(OperationCanceledException){}
            catch{if(!request.IsCancellationRequested)status.Text="Не удалось загрузить подборку. Попробуй открыть её позже.";}
        };
        dialog.ShowDialog();
    }
}
