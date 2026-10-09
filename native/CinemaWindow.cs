using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace Kachalka;

public partial class MainWindow
{
    sealed class CrewViewportHeight:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>
            Math.Clamp(value is double height?height*.7:280,220,520);
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    static readonly CrewViewportHeight crewViewportHeight=new();
    Border RenderCinemaConnections(StackPanel panel,MediaItem item,Panel? participantsHost=null)
    {
        var people=new StackPanel();
        if(item.People.Length==0)
        {
            people.Children.Add(Text(DetailMetadataLoading(item.Id)?"Загружаем участников…":"Участники пока недоступны у источника.",12,true));
            if(DetailMetadataNeedsRetry(item.Id)){var retry=ActionButton("Загрузить участников","IconRefresh",()=>RetryDetailMetadata(item));retry.HorizontalAlignment=HorizontalAlignment.Left;people.Children.Add(retry);}
        }
        if(item.People.Length>0)
        {
            var row=new UniformGrid{Name="CinemaPeopleGrid",Columns=3,Margin=new(0,0,-12,0)};people.Children.Add(row);
            row.SizeChanged+=(_,_)=>{var columns=Math.Clamp((int)Math.Floor(row.ActualWidth/124),1,8);if(row.Columns!=columns)row.Columns=columns;};
            foreach(var person in item.People)
            {
                var button=Button(person.Name,()=>OpenPerson(person,item));button.Name="CinemaPersonTile";button.Tag=person;button.Style=(Style)FindResource("PosterButton");
                var tile=new StackPanel();var portrait=PersonPortrait(person,96,120,item);portrait.Name="CinemaPersonPortrait";portrait.BorderThickness=new(0);portrait.HorizontalAlignment=HorizontalAlignment.Center;tile.Children.Add(portrait);var name=Text(person.Name,12);name.TextAlignment=TextAlignment.Center;name.MinHeight=28;name.MaxHeight=32;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new(0);tile.Children.Add(name);
                var role=Text(person.Role switch{"Актёры"=>"В ролях","Режиссёры"=>"Режиссёр","Операторы"=>"Оператор",_=>person.Role},10,true);role.TextAlignment=TextAlignment.Center;role.TextWrapping=TextWrapping.NoWrap;role.TextTrimming=TextTrimming.CharacterEllipsis;role.Margin=new(0,3,0,0);tile.Children.Add(role);button.Content=tile;
                button.Padding=new(4);button.Margin=new(0,0,12,12);button.BorderThickness=new(0);button.MinHeight=0;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;button.ToolTip=person.Name+" · "+person.Role;
                AutomationProperties.SetName(button,"Открыть карточку: "+person.Name+", "+person.Role);row.Children.Add(button);
            }
        }
        var participantContent=new Grid();participantContent.RowDefinitions.Add(new(){Height=GridLength.Auto});participantContent.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var heading=Text("Актёры и съёмочная группа",18);heading.Name="CinemaPeopleHeading";heading.FontWeight=FontWeights.Bold;heading.Margin=new(0,0,0,16);participantContent.Children.Add(heading);
        var participantScroll=new ScrollViewer{Name="CinemaPeopleScroll",Style=(Style)FindResource("PageScroll"),Content=people,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(participantScroll,1);participantContent.Children.Add(participantScroll);
        BindingOperations.SetBinding(participantScroll,FrameworkElement.MaxHeightProperty,new Binding(nameof(ActualHeight)){Source=Body,Converter=crewViewportHeight,Mode=BindingMode.OneWay});
        var participants=new Border{Name="CinemaParticipants",Child=participantContent,CornerRadius=new(20),Padding=new(24),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Top};
        participants.SetResourceReference(Border.BackgroundProperty,"Panel");participants.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
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
        return participants;
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
    (CinemaPerson Person,MediaItem? Origin)? returnPerson;
    void OpenPerson(CinemaPerson person,MediaItem? origin)
    {
        activePerson=person;personOrigin=origin;section=origin?.Section??lastCatalogSection;personSection=section;current=origin;Render();
    }
    void CinemaBack()
    {
        if(returnPerson is {} previous){returnPerson=null;OpenPerson(previous.Person,previous.Origin);return;}
        if(ReturnToSaved())return;
        if(section=="Загрузки"||DownloadBackLabel()!=null)
        {
            section="Загрузки";current=null;activePerson=null;personOrigin=null;personSearchReturn=null;Render();return;
        }
        if(catalogBrowseContext is {} catalog){RestoreCatalogBrowseContext(catalog);Render();return;}
        current=null;Render();
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
