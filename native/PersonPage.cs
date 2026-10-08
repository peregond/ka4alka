using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    void RenderPerson(CinemaPerson person,MediaItem? origin)
    {
        PageHeader.Children.Add(ActionButton(origin==null?"К результатам поиска":"Назад к фильму","IconBack",()=>
        {
            activePerson=null;
            if(origin==null){RestorePersonSearch();return;}
            current=personOrigin??origin;Render();
        },"QuietButton"));
        var body=new StackPanel{Margin=new(0,0,10,0)};
        var request=personRequest=new CancellationTokenSource();
        var scroll=new ScrollViewer{Name="PersonPageScroll",Style=(Style)FindResource("PageScroll"),Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Body.Children.Add(scroll);
        var hero=new Grid{Name="PersonHeroContent"};hero.ColumnDefinitions.Add(new(){Width=new(174)});hero.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        for(var row=0;row<3;row++)hero.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var portrait=PersonPortrait(person,150,190,origin);portrait.Name="PersonPortrait";portrait.VerticalAlignment=VerticalAlignment.Top;portrait.HorizontalAlignment=HorizontalAlignment.Left;portrait.Margin=new(0,0,24,0);hero.Children.Add(portrait);
        var identity=new StackPanel{Name="PersonIdentity"};Grid.SetColumn(identity,1);hero.Children.Add(identity);
        var role=Text(person.Role,12,true);role.Margin=new(0,0,0,7);identity.Children.Add(role);
        var name=Text(person.Name,30);name.Name="PersonName";name.FontWeight=FontWeights.SemiBold;name.Margin=new(0,0,0,16);identity.Children.Add(name);
        var biographyPanel=new StackPanel{Name="PersonBiographyPanel"};identity.Children.Add(biographyPanel);
        var biography=Text("Загружаем биографию…",14,true);biography.Name="PersonBiography";biography.LineHeight=21;biography.MaxHeight=126;biography.Margin=new(0);biographyPanel.Children.Add(biography);
        bool expanded=false;
        Button expand=null!;expand=Button("Подробнее",()=>{expanded=!expanded;UpdateBiography();});expand.Name="PersonBiographyToggle";expand.Style=(Style)FindResource("QuietButton");expand.FontSize=12;expand.HorizontalAlignment=HorizontalAlignment.Left;expand.Padding=new(0,5,5,5);expand.Margin=new(0,5,0,0);expand.Visibility=Visibility.Collapsed;biographyPanel.Children.Add(expand);
        string? sourceUrl=null;
        var source=Button("Источник биографии",()=>{if(sourceUrl!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourceUrl){UseShellExecute=true});});source.Name="PersonBiographySource";source.Style=(Style)FindResource("QuietButton");source.FontSize=11;source.HorizontalAlignment=HorizontalAlignment.Left;source.Padding=new(0,4,5,4);source.Margin=new(0,6,0,0);source.Visibility=Visibility.Collapsed;biographyPanel.Children.Add(source);
        var frame=PersonPanel(hero,"PersonHero");frame.Padding=new(22);frame.Margin=new(0,0,0,20);body.Children.Add(frame);
        void UpdateBiography()
        {
            biography.MaxHeight=expanded?double.PositiveInfinity:126;
            expand.Content=expanded?"Свернуть":"Подробнее";
            if(biography.ActualWidth<=0)return;
            var formatted=new FormattedText(biography.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(biography.FontFamily,biography.FontStyle,biography.FontWeight,biography.FontStretch),biography.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(biography).PixelsPerDip){MaxTextWidth=Math.Max(1,biography.ActualWidth),LineHeight=21};
            expand.Visibility=formatted.Height>127?Visibility.Visible:Visibility.Collapsed;
        }
        void HeroLayout()
        {
            if(hero.ActualWidth<=0)return;
            var narrow=hero.ActualWidth<600;var tiny=hero.ActualWidth<280;
            portrait.Width=narrow?112:150;portrait.Height=narrow?140:190;hero.ColumnDefinitions[0].Width=new(narrow?132:174);name.FontSize=narrow?24:30;
            Grid.SetRow(identity,tiny?1:0);Grid.SetColumn(identity,tiny?0:1);Grid.SetColumnSpan(identity,tiny?2:1);identity.Margin=tiny?new(0,16,0,0):new(0);
            Grid.SetColumnSpan(portrait,tiny?2:1);portrait.Margin=new(0,0,narrow?20:24,0);
            if(narrow)
            {
                if(identity.Children.Contains(biographyPanel)){identity.Children.Remove(biographyPanel);hero.Children.Add(biographyPanel);}
                Grid.SetColumn(biographyPanel,0);Grid.SetRow(biographyPanel,tiny?2:1);Grid.SetColumnSpan(biographyPanel,2);biographyPanel.Margin=new(0,16,0,0);
            }
            else
            {
                if(hero.Children.Contains(biographyPanel)){hero.Children.Remove(biographyPanel);identity.Children.Add(biographyPanel);}
                biographyPanel.Margin=new(0);Grid.SetColumnSpan(biographyPanel,1);
            }
            UpdateBiography();
        }
        hero.SizeChanged+=(_,_)=>HeroLayout();biography.SizeChanged+=(_,_)=>UpdateBiography();
        var status=Text("Загружаем фильмографию…",12,true);status.Name="PersonFilmographyStatus";status.Margin=new(2,0,0,14);body.Children.Add(status);
        var top=PersonGallery("Лучшие фильмы по Кинопоиску","Оценки среди подтверждённых работ участника.","PersonTopFilms");body.Children.Add(top.Frame);
        var awards=PersonGallery("Фильмы с наградами","Награды за работу этого участника.","PersonAwardFilms");body.Children.Add(awards.Frame);
        var all=PersonGallery("Фильмография","Фильмы и сериалы · сначала новые.","PersonFilmographyGrid");body.Children.Add(all.Frame);
        var empty=Text("Подтверждённые фильмы и сериалы пока недоступны.",13,true);empty.Name="PersonFilmographyEmpty";empty.Visibility=Visibility.Collapsed;body.Children.Add(empty);
        int refreshAttempt=0,settledAttempt=0;
        Button retry=null!;retry=ActionButton("Повторить загрузку","IconRefresh",()=>{_ = Refresh();});retry.Name="PersonRetry";retry.HorizontalAlignment=HorizontalAlignment.Left;retry.Visibility=Visibility.Collapsed;body.Children.Add(retry);
        bool IsCurrent()=>!closed&&!request.IsCancellationRequested&&ReferenceEquals(personRequest,request)&&activePerson==person;
        void Apply(PersonProfile profile,int attempt,bool complete=false)
        {
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Apply(profile,attempt,complete)),DispatcherPriority.DataBind);return;}
            // A provider's queued progress can arrive after its final result or
            // after a retry started. Check again on the UI thread before editing.
            if(!IsCurrent()||attempt!=refreshAttempt||!complete&&attempt==settledAttempt)return;
            if(complete)settledAttempt=attempt;
            var hasBiography=!string.IsNullOrWhiteSpace(profile.Description);
            if(hasBiography||complete)
            {
                biography.Text=hasBiography?profile.Description:"Биография пока недоступна.";biography.SetResourceReference(TextBlock.ForegroundProperty,hasBiography?"Text":"Muted");UpdateBiography();
            }
            var uri=PersonBiographyUrl(profile.SourceUrl);sourceUrl=uri?.AbsoluteUri;source.Visibility=uri==null?Visibility.Collapsed:Visibility.Visible;source.ToolTip=uri?.Host;
            var films=profile.Filmography.Where(film=>film.Cinema).DistinctBy(film=>film.Id).OrderByDescending(film=>film.Year).ThenBy(film=>film.Title).ToArray();
            all.Update(films);all.Frame.Visibility=films.Length>0?Visibility.Visible:Visibility.Collapsed;
            var best=CinemaMetadata.Top(films);top.Update(best);top.Frame.Visibility=films.Length>3&&best.Length>1?Visibility.Visible:Visibility.Collapsed;
            var winners=films.Where(film=>film.Awards.Any(award=>award.Winner&&person.PageUrl.Length>0&&award.PersonUrl==person.PageUrl)).ToArray();awards.Update(winners);awards.Frame.Visibility=winners.Length>0?Visibility.Visible:Visibility.Collapsed;
            status.Text=complete?(films.Length>0?$"Подтверждённые работы: {films.Length}":"Фильмография пока недоступна."):(films.Length>0?$"Подтверждённые работы: {films.Length} · загружаем остальные…":"Загружаем фильмографию…");
            empty.Visibility=complete&&films.Length==0?Visibility.Visible:Visibility.Collapsed;
            retry.Visibility=complete&&(!hasBiography||films.Length<2)?Visibility.Visible:Visibility.Collapsed;
        }
        async Task Refresh()
        {
            if(!IsCurrent())return;
            var attempt=++refreshAttempt;
            retry.Visibility=Visibility.Collapsed;status.Text="Загружаем фильмографию…";
            try
            {
                var profile=await new LiveCatalog(sourceClient).Person(person,request.Token,origin,liveItems.Concat(prefs.LiveFavorites),updated=>Apply(updated,attempt));Apply(profile,attempt,true);
            }
            catch(OperationCanceledException)when(request.IsCancellationRequested){}
            catch
            {
                if(!IsCurrent()||attempt!=refreshAttempt)return;settledAttempt=attempt;status.Text="Не удалось обновить фильмографию.";retry.Visibility=Visibility.Visible;
                if(biography.Text=="Загружаем биографию…")biography.Text="Биография пока недоступна.";
            }
        }
        all.Frame.Visibility=top.Frame.Visibility=awards.Frame.Visibility=Visibility.Collapsed;
        _=Refresh();
    }

    static Uri? PersonBiographyUrl(string? value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0)return null;
        if(uri.Host=="ru.wikipedia.org"&&uri.AbsolutePath.StartsWith("/wiki/",StringComparison.Ordinal))return uri;
        if(uri.Host is "kino-teatr.ua" or "www.kino-teatr.ua")return uri;
        return CinemaMetadata.CatalogUrl(value,"/persons/","/person/","/people/")==null?null:uri;
    }
    Border PersonPanel(UIElement child,string name)
    {
        var panel=new Border{Name=name,Child=child,CornerRadius=new(22),Padding=new(18),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Top};panel.SetResourceReference(Border.BackgroundProperty,"Panel");panel.SetResourceReference(Border.BorderBrushProperty,"Edge");return panel;
    }
}
