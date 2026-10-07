using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace Kachalka;

public partial class MainWindow
{
    void RenderCinemaConnections(StackPanel panel,MediaItem item)
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
                AutomationProperties.SetName(button,"Открыть карточку: "+person.Name+", "+person.Role);row.Children.Add(button);
            }
        }
        panel.Children.Add(people);
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
    void OpenPerson(CinemaPerson person,MediaItem origin)
    {
        var (dialog,body)=CinemaDialog(person.Name,person.Role);using var request=new CancellationTokenSource();
        dialog.Closed+=(_,_)=>request.Cancel();
        async Task Load()
        {
            var content=new StackPanel();body.Children.Add(content);
            async Task Refresh()
            {
                content.Children.Clear();content.Children.Add(Text("Загружаем биографию и фильмографию…",13,true));
                try
                {
                    var profile=await new LiveCatalog(sourceClient).Person(person,request.Token);
                    if(request.IsCancellationRequested)return;
                    content.Children.Clear();content.Children.Add(Text(profile.Description.Length>0?profile.Description:"Биография пока недоступна.",13,true));
                    CinemaFilms(content,"Выбор зрителей · топ по Кинопоиску",CinemaMetadata.Top(profile.Filmography),dialog);
                    content.Children.Add(Text("Рейтинг Кинопоиска среди фильмов, предоставленных источником.",11,true));
                    var known=profile.Filmography.Select(x=>x.Id).ToHashSet();
                    var awarded=liveItems.Concat(prefs.LiveFavorites).Append(origin).Where(x=>known.Contains(x.Id)&&x.Awards.Any(a=>a.Winner&&a.PersonUrl==person.PageUrl)).DistinctBy(x=>x.Id);
                    CinemaFilms(content,"Фильмы с наградами за работу этого участника",awarded,dialog);
                    CinemaFilms(content,"Фильмография",profile.Filmography.OrderByDescending(x=>x.Year),dialog);
                }
                catch(OperationCanceledException){}
                catch
                {
                    if(request.IsCancellationRequested)return;
                    content.Children.Clear();content.Children.Add(Text("Не удалось загрузить карточку человека.",13,true));
                    content.Children.Add(Button("Повторить",()=>{_ = Refresh();}));
                }
            }
            await Refresh();
        }
        dialog.Loaded+=async(_,_)=>await Load();dialog.ShowDialog();
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
