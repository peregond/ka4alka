using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace Kachalka;

public partial class MainWindow
{
    void RenderCinemaConnections(StackPanel panel,MediaItem item,Panel? participantsHost=null)
    {
        var people=new StackPanel{Margin=new(0,0,0,20)};
        people.Children.Add(Text("Актёры и съёмочная группа",18));
        if(item.People.Length==0)people.Children.Add(Text(item.Description==null?"Загружаем участников…":"Источник пока не предоставил участников съёмочной группы.",12,true));
        foreach(var group in item.People.GroupBy(x=>x.Role))
        {
            people.Children.Add(Text(group.Key,12,true));var row=new WrapPanel();people.Children.Add(row);
            foreach(var person in group)
            {
                var button=Button(person.Name,()=>OpenPerson(person,item));button.Style=(Style)FindResource("PillButton");
                var tile=new StackPanel{Width=116};tile.Children.Add(PersonPortrait(person,104,130));tile.Children.Add(Text(person.Name,12));button.Content=tile;button.Margin=new(0,0,10,12);
                AutomationProperties.SetName(button,"Открыть карточку: "+person.Name+", "+person.Role);row.Children.Add(button);
            }
        }
        var participants=new Border{Name="CinemaParticipants",Child=new ScrollViewer{Content=people,MaxHeight=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},CornerRadius=new(22),Padding=new(20),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Top};
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
    Border PersonPortrait(CinemaPerson person,double width,double height)
    {
        var grid=new Grid();var fallback=Text(string.Join("",person.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x=>x[0])),24,true);
        fallback.HorizontalAlignment=HorizontalAlignment.Center;fallback.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(fallback);
        var image=new Image{Stretch=Stretch.UniformToFill};grid.Children.Add(image);
        var frame=new Border{Width=width,Height=height,CornerRadius=new(11),ClipToBounds=true,Child=grid,Margin=new(0,0,0,8)};frame.SetResourceReference(Border.BackgroundProperty,"Selected");frame.SizeChanged+=(_,_)=>ClipPoster(frame);
        CancellationTokenSource? request=null;
        image.Unloaded+=(_,_)=>request?.Cancel();
        image.Loaded+=async(_,_)=>
        {
            request?.Cancel();using var pending=new CancellationTokenSource(TimeSpan.FromSeconds(30));request=pending;
            try
            {
                var url=await new CinemaPeople(sourceClient).Portrait(person,pending.Token);if(url==null)return;
                var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
                var (bitmap,_)=await CoverCache.Load(Path.Combine(Preferences.DataDir,"portraits",key+".img"),2*1024*1024,
                    ct=>sourceClient.Read(new Uri(url),2*1024*1024,ct),bytes=>{using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=400;result.StreamSource=stream;result.EndInit();result.Freeze();return result;},pending.Token);
                if(image.IsLoaded&&!pending.IsCancellationRequested)image.Source=bitmap;
            }
            catch(OperationCanceledException){}catch{}
            finally{if(ReferenceEquals(request,pending))request=null;}
        };
        return frame;
    }
    void RenderPerson(CinemaPerson person,MediaItem origin)
    {
        PageHeader.Children.Add(ActionButton("Назад к фильму · "+origin.Title,"IconBack",()=>{activePerson=null;current=origin;Render();},"QuietButton"));
        var body=new StackPanel{Margin=new(0,0,10,0)};
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var hero=new WrapPanel();hero.Children.Add(PersonPortrait(person,150,190));
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
                var source=Button("Биография и фотографии: Википедия / Wikimedia",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourceUrl){UseShellExecute=true}));source.Style=(Style)FindResource("QuietButton");content.Children.Add(source);
                void Films(string title,IEnumerable<MediaItem> items)
                {
                    content.Children.Add(Text(title,18));var films=items.ToArray();if(films.Length==0){content.Children.Add(Text("Данные пока недоступны.",12,true));return;}
                    foreach(var film in films)
                    {
                        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(64)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                        var poster=new Image{Width=48,Height=72,DataContext=film,Stretch=Stretch.Uniform};poster.Loaded+=SourceCover;row.Children.Add(poster);
                        var label=Text(film.Title+" · "+film.Year+(CinemaMetadata.Rating(film)>0?" · КП "+film.Kinopoisk:""),14);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
                        var button=Button(film.Title,()=>{returnPerson=(person,origin);activePerson=null;current=film;section=film.Section;Render();});button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.HorizontalAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(button,"Открыть "+film.Title);content.Children.Add(button);
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
