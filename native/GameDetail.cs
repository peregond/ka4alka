using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Kachalka;

// A game's page is laid out like a film's: a banner with the cover, identity and the main action, a card with
// the facts, and below it the releases with one recommended choice.
public partial class MainWindow
{
    sealed class GamePickerState
    {
        public string Sort {get;set;}=AppLanguage.Current=="en"?"По языку интерфейса":"Больше отдающих";
        public string Source {get;set;}="Все";
        public int VisibleCount {get;set;}=40;
        public bool Reverse {get;set;}
    }
    readonly Dictionary<int,GamePickerState> gamePickerStates=[];
    ScrollViewer? gameScroll;
    Grid? gameHeroGrid,gameAboutGrid;
    Border? gameCapsule,gameAbout;
    StackPanel? gameInfoPanel,gameReleaseHost,gameCaption;
    WrapPanel? gameMetaRow;
    Panel? gameActions;
    FrameworkElement? gameSynopsisSkeleton,gameReleaseAnchor;
    TextBlock? gameReleaseCount,gameTraits;
    string gameActionState="";
    int gameSteamId;
    // The screenshot gallery under the banner: one large picture, the strip of thumbnails and the request that fills them.
    StackPanel? gameGallery;
    GameShot[] gameShots=[];int gameShotIndex;
    Image? gameShotImage;TextBlock? gameShotCounter;StackPanel? gameShotStrip;ScrollViewer? gameShotScroll;
    CancellationTokenSource? gameGalleryRequest;

    void RenderGameDetail(MediaItem game)
    {
        SetBack(FeaturedBackLabel()??SavedBackLabel()??DownloadBackLabel()??"Игры",GameBack);
        if(!GameCatalog.TryId(game.PageUrl,out var steamId)){PageHeader.Children.Add(Text("Не удалось открыть карточку игры: неизвестный адрес Steam.",14,true));return;}
        gameSteamId=steamId;gameDetails.TryGetValue(game.Id,out var detail);var item=detail?.Item??game;
        if(descriptionItemId!=game.Id){descriptionItemId=game.Id;descriptionExpanded=false;}
        gameActionState="";gameSynopsisSkeleton=null;
        // The scroll viewer extends 6 px past the gutter so focus rings are not clipped.
        var panel=new StackPanel{Margin=new(6,6,6,0)};
        gameScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,Margin=new(-6,-6,-6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Body.Children.Add(gameScroll);

        // ---- banner ----
        var heroFrame=new Border{Name="GameHero",CornerRadius=new(24),VerticalAlignment=VerticalAlignment.Top};heroFrame.Background=(Brush)FindResource("BannerBase");
        var artwork=new Grid{Name="GameArtwork"};heroFrame.Child=artwork;
        artwork.SizeChanged+=(_,e)=>{if(e.NewSize.Width<=0||e.NewSize.Height<=0)return;var clip=new RectangleGeometry(new Rect(e.NewSize),24,24);clip.Freeze();artwork.Clip=clip;};
        // The wide picture is Steam's own hero art; where a game has none the blurred cover fills the banner.
        var backdrop=new Image{DataContext=game,Width=0,Height=0,Opacity=0,Tag="FeaturePoster"};if(!prefs.LiteMode){backdrop.Loaded+=SourceCover;backdrop.DataContextChanged+=SourceCoverChanged;}artwork.Children.Add(backdrop);
        var cover=gameCover=new Image{DataContext=item,Stretch=Stretch.UniformToFill};cover.Loaded+=SourceCover;cover.DataContextChanged+=SourceCoverChanged;
        if(!prefs.LiteMode)AddBannerArtwork(artwork,backdrop,cover);
        var hero=gameHeroGrid=new Grid{Name="GameHeroContent",Margin=new(32,28,32,28)};
        hero.ColumnDefinitions.Add(new(){Width=GridLength.Auto});hero.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});hero.RowDefinitions.Add(new(){Height=GridLength.Auto});
        artwork.Children.Add(hero);
        var coverGrid=new Grid();
        coverGrid.Children.Add(new TextBlock{Text=AppLanguage.T("Обложка\nнедоступна"),Foreground=(Brush)FindResource("BannerMuted"),Opacity=.75,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=11});
        coverGrid.Children.Add(cover);
        gameCapsule=new Border{Name="GameCapsule",Width=135,Height=202.5,CornerRadius=new(14),ClipToBounds=true,Background=item.Cover,BorderBrush=(Brush)FindResource("BannerChip"),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,28,0),Child=coverGrid};
        gameCapsule.SizeChanged+=(sender,_)=>ClipPoster((Border)sender);
        hero.Children.Add(gameCapsule);

        var info=gameInfoPanel=new StackPanel{Name="GameIdentity",VerticalAlignment=VerticalAlignment.Bottom};Grid.SetColumn(info,1);hero.Children.Add(info);
        gameCaption=new StackPanel{Name="GameCaption",Orientation=Orientation.Horizontal,Margin=new(0,0,0,12)};info.Children.Add(gameCaption);
        gameTitle=new TextBlock{Name="GameTitle",FontSize=48,FontWeight=FontWeights.ExtraBold,LineHeight=50,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("BannerText"),Margin=new(0,0,0,12)};info.Children.Add(gameTitle);
        gameMetaRow=new WrapPanel{Name="GameMetaRow",Margin=new(0,0,0,10)};info.Children.Add(gameMetaRow);
        gameTraits=new TextBlock{Name="GameTraits",FontSize=14,Foreground=(Brush)FindResource("BannerMuted"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};info.Children.Add(gameTraits);
        RenderGameHeading(item,detail);

        var description=new StackPanel{Name="GameDescription",Margin=new(0,0,0,16)};info.Children.Add(description);
        var synopsis=gameDescription=detailSynopsis=new TextBlock{Name="GameSynopsis",Text=AppLanguage.T(detail!=null?GameDescriptionText(item):"Загружаем описание…"),FontSize=15,LineHeight=23,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=(Brush)FindResource("BannerMuted"),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=descriptionExpanded?double.PositiveInfinity:69};
        description.Children.Add(synopsis);
        // While the description is on its way its place breathes instead of saying "loading".
        if(detail==null){synopsis.Visibility=Visibility.Collapsed;gameSynopsisSkeleton=SynopsisSkeleton();description.Children.Add(gameSynopsisSkeleton);}
        descriptionToggle=Button(descriptionExpanded?"Свернуть описание":"Читать дальше",()=>
        {
            if(descriptionItemId!=game.Id||detailSynopsis==null)return;
            descriptionExpanded=!descriptionExpanded;detailSynopsis.MaxHeight=descriptionExpanded?double.PositiveInfinity:69;UpdateDescriptionToggle();
        });descriptionToggle.Name="DescriptionToggle";descriptionToggle.Style=(Style)FindResource("QuietButton");descriptionToggle.Foreground=(Brush)FindResource("BannerText");descriptionToggle.HorizontalAlignment=HorizontalAlignment.Left;descriptionToggle.Padding=new(0,5,0,5);descriptionToggle.Margin=new(0);descriptionToggle.MinHeight=28;descriptionToggle.Visibility=Visibility.Collapsed;description.Children.Add(descriptionToggle);
        synopsis.SizeChanged+=(sender,_)=>{if(ReferenceEquals(sender,detailSynopsis))UpdateDescriptionToggle();};

        gameActions=new WrapPanel{Name="GameActions",Margin=new(0,0,0,-8)};info.Children.Add(gameActions);
        BuildGameActions(item);

        var cards=new Grid{Name="GameCards",Margin=new(0,0,0,28)};for(var row=0;row<3;row++)cards.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.Children.Add(cards);cards.Children.Add(heroFrame);
        gameGallery=new StackPanel{Name="GameGallery",Margin=new(0,24,0,0),Visibility=Visibility.Collapsed};Grid.SetRow(gameGallery,1);cards.Children.Add(gameGallery);
        gameAbout=new Border{Name="GameAbout",CornerRadius=new(20),Padding=new(24),BorderThickness=new(1),Margin=new(0,20,0,0),VerticalAlignment=VerticalAlignment.Top};
        gameAbout.SetResourceReference(Border.BackgroundProperty,"Panel");gameAbout.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
        Grid.SetRow(gameAbout,2);cards.Children.Add(gameAbout);
        RenderGameAbout(item,detail);RenderGameGallery(detail);

        // ---- releases ----
        var titleRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,16)};gameReleaseAnchor=titleRow;
        var releasesTitle=Text("Раздачи",22);releasesTitle.FontWeight=FontWeights.Bold;releasesTitle.Margin=new(0);releasesTitle.VerticalAlignment=VerticalAlignment.Center;titleRow.Children.Add(releasesTitle);
        gameReleaseCount=new TextBlock{Name="GameReleaseCount",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,Margin=new(10,3,0,0),VerticalAlignment=VerticalAlignment.Center};gameReleaseCount.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");titleRow.Children.Add(gameReleaseCount);
        panel.Children.Add(titleRow);
        gameReleaseHost=new StackPanel{Name="GameReleases"};panel.Children.Add(gameReleaseHost);
        if(gameDetailId!=game.Id){gameDetailRequest?.Cancel();gameDetailId=game.Id;gameReleases=gameReleaseCache.GetValueOrDefault(game.Id)??[];gameSourceChecks=[];gameReleasesLoading=true;gameDetailRequest=new();_=LoadGameDetail(game,gameDetailRequest);}
        UpdateGameDetailLayout();
        RenderGameReleases();
    }
    static string GameDescriptionText(MediaItem item)=>string.IsNullOrWhiteSpace(item.Description)?"Описание пока недоступно.":item.Description;

    // The banner is always dark, so the review colours are the bright ones of the dark theme in either theme.
    static readonly Brush ToneGood=Frozen("#5BE3B5"),ToneMixed=Frozen("#F2B35B"),ToneBad=Frozen("#FF8A7A");
    static Brush Frozen(string color){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));brush.Freeze();return brush;}
    static Brush ToneInk(string tone)=>tone switch{"mixed"=>ToneMixed,"negative"=>ToneBad,_=>ToneGood};
    // "👍 93% · Очень положительные": Steam's verdict on the game, in the colour of that verdict.
    FrameworkElement GameReviewChip(int percent,string tone,string label,int count)
    {
        var chip=new StackPanel{Name="GameReview",Orientation=Orientation.Horizontal,Margin=new(0,0,14,4),ToolTip=AppLanguage.T((label.Length>0?label+" · ":"")+percent+"% положительных отзывов Steam"+(count>0?" из "+GameNumbers.Count(count):""))};
        AutomationProperties.SetName(chip,AppLanguage.T("Отзывы Steam: "+percent+"%"));
        chip.Children.Add(new TextBlock{Text="👍",FontSize=13,Foreground=ToneInk(tone),Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center});
        chip.Children.Add(new TextBlock{Text=percent+"%",FontSize=14,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center});
        if(label.Length>0)chip.Children.Add(new TextBlock{Text="  "+label,FontSize=12,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center});
        return chip;
    }
    Border GameBadge(string text,Brush? ink=null,string? tip=null,string? name=null)
    {
        var badge=new Border{BorderBrush=ink??(Brush)FindResource("BannerBadgeEdge"),BorderThickness=new(1),CornerRadius=new(6),Padding=new(6,2,6,2),Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,ToolTip=tip,Child=new TextBlock{Text=AppLanguage.T(text),FontSize=11,FontWeight=FontWeights.Bold,Foreground=ink??(Brush)FindResource("BannerText")}};
        if(name!=null)badge.Name=name;return badge;
    }
    // A discount is a filled green block in front of the price, as it is on the cards.
    FrameworkElement GamePriceBadge(string price,int discount,string original)
    {
        var row=new StackPanel{Name="GamePrice",Orientation=Orientation.Horizontal,Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,ToolTip=AppLanguage.T(discount>0&&original.Length>0?"Скидка "+discount+"%. Обычная цена — "+original:"Цена в Steam")};
        if(discount>0)
        {
            var block=new Border{CornerRadius=new(6,0,0,6),Padding=new(6,2,6,2)};block.SetResourceReference(Border.BackgroundProperty,"Primary");
            var label=new TextBlock{Text="−"+discount+"%",FontSize=11,FontWeight=FontWeights.Bold};label.SetResourceReference(TextBlock.ForegroundProperty,"PrimaryInk");block.Child=label;row.Children.Add(block);
        }
        row.Children.Add(new Border{BorderBrush=(Brush)FindResource("BannerBadgeEdge"),BorderThickness=new(1),CornerRadius=discount>0?new(0,6,6,0):new(6),Padding=new(6,2,6,2),Child=new TextBlock{Text=AppLanguage.T(price),FontSize=11,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText")}});
        return row;
    }
    static (string Short,string Long) RussianText(GameRussian russian)=>russian switch
    {
        GameRussian.Voiced=>("Русская озвучка","В игре есть русский интерфейс, субтитры и озвучка."),
        GameRussian.Interface=>("Русский язык","В игре есть русский интерфейс и субтитры, озвучки нет."),
        GameRussian.Absent=>("Нет русского","Steam не указывает русский язык. Раздачи с переводом смотри ниже."),
        _=>("","")
    };

    // The caption, title and rating line change when the store details arrive.
    void RenderGameHeading(MediaItem item,GameDetails? detail)
    {
        var store=item.Store;
        if(gameCaption!=null)
        {
            gameCaption.Children.Clear();
            void Part(string text,string? name=null)
            {
                var label=new TextBlock{Text=AppLanguage.T(text),FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center};if(name!=null)label.Name=name;gameCaption.Children.Add(label);
            }
            Part("ИГРА");
            if(detail is {ComingSoon:true,ReleaseDate.Length:>0}){Part("  ·  ");Part("ВЫЙДЕТ "+detail.ReleaseDate.Replace(" г.","").ToUpperInvariant(),"GameYear");}
            else if(item.Year>0){Part("  ·  ");Part(item.Year.ToString(),"GameYear");}
            Part("  ·  ");Part("WINDOWS");
        }
        if(gameTitle!=null){gameTitle.Text=item.Title;gameTitle.ToolTip=item.Title;}
        if(gameMetaRow==null)return;
        gameMetaRow.Children.Clear();
        int percent=0,count=0;var tone="";var verdict="";
        if(detail?.Review is {} reviewed){percent=reviewed.Percent;count=reviewed.Count;tone=reviewed.Tone;verdict=reviewed.Label;}
        else if(store is {HasReview:true}){percent=store.ReviewPercent;count=store.ReviewCount;tone=store.ReviewTone;verdict=store.ReviewLabel;}
        if(percent>0&&tone.Length>0)gameMetaRow.Children.Add(GameReviewChip(percent,tone,verdict,count));
        if(detail is {Metacritic:>0})
        {
            var rating=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,14,4),ToolTip=AppLanguage.T("Оценка критиков на Metacritic · "+detail.Metacritic)};
            var star=new TextBlock{Text="★",FontSize=14,Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};star.SetResourceReference(TextBlock.ForegroundProperty,"RatingInk");rating.Children.Add(star);
            rating.Children.Add(new TextBlock{Text=AppLanguage.T(detail.Metacritic.ToString()),FontSize=14,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center});
            rating.Children.Add(new TextBlock{Text=" Metacritic",FontSize=12,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center});
            gameMetaRow.Children.Add(rating);
        }
        var traits=string.Join(" · ",new[]{item.Genre!="Windows"?item.AllGenres:"",detail?.Developers??""}.Where(x=>!string.IsNullOrWhiteSpace(x)));
        if(gameTraits!=null){gameTraits.Text=AppLanguage.T(traits);gameTraits.Visibility=traits.Length>0?Visibility.Visible:Visibility.Collapsed;}
        gameMetaRow.Children.Add(GameBadge("Windows"));
        if(detail!=null&&RussianText(detail.Russian) is {Short.Length:>0} russian)gameMetaRow.Children.Add(GameBadge(russian.Short,detail.Russian==GameRussian.Absent?null:ToneGood,russian.Long,"GameRussian"));
        if(detail is {Age:>=12})gameMetaRow.Children.Add(GameBadge(detail.Age+"+",null,"Возрастное ограничение в Steam"));
        var price=detail is {Price.Length:>0}?detail.Price:store is {HasPrice:true}?store.Price:"";
        if(price.Length>0)
        {
            var discount=detail is {Price.Length:>0}?detail.Discount:store?.Discount??0;var original=detail is {Price.Length:>0}?detail.OriginalPrice:store?.OriginalPrice??"";
            gameMetaRow.Children.Add(GamePriceBadge(price,discount,original));
        }
    }

    // ---------- main action: follows the state of the game ----------
    DownloadItem[] GameDownloads(MediaItem item)=>DownloadsOf(item);
    string GameActionState(MediaItem item)
    {
        var owned=GameDownloads(item);
        if(owned.Any(x=>x.Completed))return "done:"+owned.First(x=>x.Completed).Id;
        if(owned.FirstOrDefault(x=>!x.Completed) is {} active)return "active:"+active.Id;
        var pick=GameReleaseRecommendation.Pick(gameReleases,prefs.UiLanguage);
        return pick!=null?"pick:"+pick.Entry.Id+"|"+gameReleases.Length:gameReleasesLoading?"loading":gameReleases.Length>0?"choose":"none";
    }
    void RefreshGameActions()
    {
        if(gameActions==null||current?.Section!="Игры")return;
        if(GameActionState(current)!=gameActionState)BuildGameActions(current);
    }
    void BuildGameActions(MediaItem item)
    {
        if(gameActions==null)return;
        gameActions.Children.Clear();gameActionState=GameActionState(item);
        var owned=GameDownloads(item);var done=owned.FirstOrDefault(x=>x.Completed);var active=owned.FirstOrDefault(x=>!x.Completed);
        var recommended=owned.Length==0?GameReleaseRecommendation.Pick(gameReleases,prefs.UiLanguage)?.Entry:null;
        Button Action(string text,string icon,bool primary,Action click)
        {
            var button=new Button{Style=(Style)FindResource(primary?"PrimaryButton":typeof(Button)),Height=52,MinHeight=52,Padding=new(22,0,22,0),FontSize=15,Margin=new(0,0,10,8)};
            if(!primary){button.SetResourceReference(Control.BackgroundProperty,"BannerAction");button.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");button.Foreground=(Brush)FindResource("BannerText");}
            button.Content=IconLabel(text,icon,18);AutomationProperties.SetName(button,AppLanguage.T(text));button.Click+=(_,_)=>click();return button;
        }
        if(done!=null)
        {
            // Nothing is installed or started for the player: the finished download opens in its folder.
            gameActions.Children.Add(Action("Показать в папке","IconFolder",true,()=>ShowDownloadInFolder(done)));
        }
        else if(active!=null)
        {
            var progress=DownloadProgressButton(active);progress.Margin=new(0,0,10,8);gameActions.Children.Add(progress);
        }
        else if(recommended!=null)
        {
            var button=new Button{Name="GameDownload",Style=(Style)FindResource("PrimaryButton"),Tag=recommended,Height=52,MinHeight=52,Padding=new(22,0,22,0),FontSize=15,Margin=new(0,0,10,8),ToolTip=AppLanguage.T("Скачать рекомендованную раздачу: "+recommended.Title)};
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            row.Children.Add(IconLabel("Скачать","IconDownload",18));
            if(recommended.Size.HasValue)row.Children.Add(new TextBlock{Text=AppLanguage.T(DownloadService.FormatBytes(recommended.Size.Value)),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,FontWeight=FontWeights.Medium,Opacity=.75,Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            button.Content=row;AutomationProperties.SetName(button,AppLanguage.T("Скачать раздачу: "+recommended.Title));button.Click+=SourceDownload;gameActions.Children.Add(button);
        }
        else if(gameReleasesLoading)
        {
            var wait=Action("Ищем раздачи…","IconRefresh",false,()=>{});wait.IsEnabled=false;gameActions.Children.Add(wait);
        }
        else if(gameReleases.Length>0)gameActions.Children.Add(Action("Выбрать раздачу","IconDownload",true,ScrollToGameReleases));
        else
        {
            var none=Action("Раздач не найдено","IconDownload",false,()=>{});none.IsEnabled=false;gameActions.Children.Add(none);
        }
        var saved=IsSaved(item);
        Button favorite=null!;favorite=new Button{Name="GameFavorite",Style=(Style)FindResource("IconButton"),Width=52,Height=52,Margin=new(0,0,10,8)};
        favorite.SetResourceReference(Control.BackgroundProperty,"BannerAction");favorite.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");favorite.Foreground=(Brush)FindResource("BannerText");
        void Paint(){var now=IsSaved(item);favorite.Content=IconLabel("",now?"IconHeartFilled":"IconHeart",20);favorite.ToolTip=AppLanguage.T(now?"Сохранено":"Сохранить");AutomationProperties.SetName(favorite,AppLanguage.T(now?"Сохранено":"Сохранить"));}
        Paint();favorite.Click+=(_,_)=>{ToggleSaved(current?.Id==item.Id?current:item);Paint();UpdateSavedCount();};
        gameActions.Children.Add(favorite);
        gameActions.Children.Add(HideTitleButton(item,"GameHide"));
    }
    void ScrollToGameReleases()
    {
        if(gameScroll?.Content is not FrameworkElement content||gameReleaseAnchor is not {IsLoaded:true} target)return;
        UpdateLayout();
        gameScroll.ScrollToVerticalOffset(Math.Max(0,target.TransformToAncestor(content).Transform(new Point()).Y-86));
    }

    // ---------- facts: requirements and the people behind the game ----------
    void RenderGameAbout(MediaItem item,GameDetails? detail)
    {
        if(gameAbout==null)return;
        var grid=gameAboutGrid=new Grid{Name="GameAboutGrid"};
        grid.ColumnDefinitions.Add(new(){Width=new GridLength(3,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        TextBlock Heading(string text){var heading=Text(text,18);heading.FontWeight=FontWeights.Bold;heading.Margin=new(0,0,0,16);return heading;}
        Grid Fact(string label,string value)
        {
            var row=new Grid{Margin=new(0,0,0,10)};row.ColumnDefinitions.Add(new(){Width=new GridLength(132)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var name=Text(label,13,true);name.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");name.Margin=new(0,1,12,0);row.Children.Add(name);
            var text=Text(value,14);text.Margin=new(0);Grid.SetColumn(text,1);row.Children.Add(text);return row;
        }

        var requirements=new StackPanel{Name="GameRequirements",Margin=new(0,0,24,0)};requirements.Children.Add(Heading("Системные требования"));
        if(detail==null)
        {
            foreach(var width in new[]{300d,420d,360d})
            {
                var bar=SkeletonBar(13,new(0,0,0,12));bar.Width=width;bar.HorizontalAlignment=HorizontalAlignment.Left;requirements.Children.Add(bar);
            }
        }
        else if(detail.Requirements.Length==0)requirements.Children.Add(Hint("Системные требования пока не указаны."));
        else
        {
            foreach(var line in detail.Requirements.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            {
                var colon=line.IndexOf(':');
                // "Минимальные:" is a heading with nothing after it; "ОС: Windows 10" is a labelled fact.
                if(colon==line.Length-1)continue;
                if(colon is >=2 and <=40)requirements.Children.Add(Fact(line[..colon],line[(colon+1)..].Trim()));
                else requirements.Children.Add(Fact("",line));
            }
        }
        grid.Children.Add(requirements);

        var about=new StackPanel{Name="GameFacts"};Grid.SetColumn(about,1);about.Children.Add(Heading("Об игре"));
        if(detail==null){var bar=SkeletonBar(13,new(0,0,0,12));bar.Width=220;bar.HorizontalAlignment=HorizontalAlignment.Left;about.Children.Add(bar);}
        else
        {
            if(detail.Developers.Length>0)about.Children.Add(Fact("Разработчик",detail.Developers));
            if(detail.Publishers.Length>0)about.Children.Add(Fact("Издатель",detail.Publishers));
            if(detail.ReleaseDate.Length>0)about.Children.Add(Fact(detail.ComingSoon?"Выйдет":"Дата выхода",detail.ReleaseDate));
            else if(item.Year>0)about.Children.Add(Fact("Год выхода",item.Year.ToString()));
            if(item.Genre!="Windows"&&item.AllGenres.Length>0)about.Children.Add(Fact("Жанры",item.AllGenres));
            if(detail.Traits.Length>0)about.Children.Add(Fact("Режимы",string.Join(", ",detail.Traits)));
            if(RussianText(detail.Russian) is {Short.Length:>0} language)about.Children.Add(Fact("Русский язык",detail.Russian switch{GameRussian.Voiced=>"Интерфейс, субтитры и озвучка",GameRussian.Interface=>"Интерфейс и субтитры, озвучки нет",_=>"Официально не поддерживается"}));
            if(detail.Review is {} review)about.Children.Add(Fact("Отзывы Steam",review.Label+" · "+review.Percent+"% из "+GameNumbers.Count(review.Count)));
            if(detail.Age>0)about.Children.Add(Fact("Возраст",detail.Age+"+"));
            if(detail.Price.Length>0)about.Children.Add(Fact("Цена в Steam",detail.Discount>0&&detail.OriginalPrice.Length>0?detail.Price+" (−"+detail.Discount+"%, обычная "+detail.OriginalPrice+")":detail.Price));
        }
        var links=new WrapPanel{Margin=new(0,8,0,0)};
        var steam=ActionButton("Открыть в Steam","IconExternal",()=>OpenGameLink(item.PageUrl!),"PillButton");steam.Name="GameSteamLink";steam.Margin=new(0,0,10,8);steam.ToolTip=AppLanguage.T("Страница игры в Steam");links.Children.Add(steam);
        var steamDb=ActionButton("SteamDB","IconExternal",()=>OpenGameLink("https://steamdb.info/app/"+gameSteamId+"/"),"PillButton");steamDb.Name="GameSteamDbLink";steamDb.Margin=new(0,0,10,8);steamDb.ToolTip=AppLanguage.T("Цены, статистика игроков и история изменений на SteamDB");links.Children.Add(steamDb);
        about.Children.Add(links);
        grid.Children.Add(about);
        gameAbout.Child=grid;UpdateGameDetailLayout();
    }
    void OpenGameLink(string address)
    {
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address){UseShellExecute=true});}
        catch(Exception error){Status.Text=AppLanguage.T("Не удалось открыть страницу: "+error.Message);}
    }

    // ---------- responsive layout ----------
    void UpdateGameDetailLayout()
    {
        if(current?.Section!="Игры")return;
        var width=Body.ActualWidth>0?Body.ActualWidth:ActualWidth;
        var narrow=width<760;var tiny=width<520;
        if(gameHeroGrid!=null&&gameCapsule!=null&&gameInfoPanel!=null)
        {
            var capsuleWidth=tiny?100d:narrow?110d:135d;
            gameCapsule.Width=capsuleWidth;gameCapsule.Height=capsuleWidth*1.5;
            gameHeroGrid.Margin=tiny?new(20,20,20,22):narrow?new(24,24,24,24):new(32,28,32,28);
            gameCapsule.Margin=tiny?new(0,0,0,18):new(0,0,narrow?20:28,0);
            // On a very narrow banner the cover moves above the text.
            Grid.SetColumn(gameInfoPanel,tiny?0:1);Grid.SetRow(gameInfoPanel,tiny?1:0);Grid.SetColumnSpan(gameInfoPanel,tiny?2:1);
            if(tiny&&gameHeroGrid.RowDefinitions.Count<2)gameHeroGrid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            if(!tiny&&gameHeroGrid.RowDefinitions.Count>1)gameHeroGrid.RowDefinitions.RemoveAt(1);
            Grid.SetColumnSpan(gameCapsule,tiny?2:1);gameCapsule.VerticalAlignment=tiny?VerticalAlignment.Top:VerticalAlignment.Center;
            if(gameTitle!=null){gameTitle.FontSize=tiny?30:narrow?38:48;gameTitle.LineHeight=tiny?33:narrow?41:50;}
        }
        if(gameAboutGrid is {Children.Count:>=2} about)
        {
            var stacked=width<860;
            Grid.SetColumn(about.Children[1],stacked?0:1);Grid.SetRow(about.Children[1],stacked?1:0);Grid.SetColumnSpan(about.Children[0],stacked?2:1);Grid.SetColumnSpan(about.Children[1],stacked?2:1);
            if(about.Children[0] is FrameworkElement left)left.Margin=stacked?new(0,0,0,20):new(0,0,24,0);
            if(stacked&&about.RowDefinitions.Count<2)about.RowDefinitions.Add(new(){Height=GridLength.Auto});
            if(!stacked&&about.RowDefinitions.Count>1)about.RowDefinitions.RemoveAt(1);
        }
    }

    // ---------- loading ----------
    // Store details and torrent sources load side by side: the page needs neither to show the other.
    async Task LoadGameDetail(MediaItem game,CancellationTokenSource request)
    {
        var token=request.Token;bool Active()=>!closed&&!token.IsCancellationRequested&&current?.Id==game.Id&&current.Section=="Игры";
        async Task Metadata()
        {
            try
            {
                var detail=gameDetails.GetValueOrDefault(game.Id)??await (gameDetailProvider?.Invoke(game,token)??new GameCatalog(sourceClient,prefs.UiLanguage).Detail(game,token));
                if(!Active())return;AddGameDetails(detail);current=detail.Item;
                if(gameCover!=null)gameCover.DataContext=detail.Item;
                RenderGameHeading(detail.Item,detail);
                if(gameDescription!=null)
                {
                    gameDescription.Text=GameDescriptionText(detail.Item);gameDescription.Visibility=Visibility.Visible;
                    if(gameSynopsisSkeleton!=null&&gameSynopsisSkeleton.Parent is Panel skeletonHost){skeletonHost.Children.Remove(gameSynopsisSkeleton);gameSynopsisSkeleton=null;}
                }
                RenderGameAbout(detail.Item,detail);RenderGameGallery(detail);
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error)
            {
                if(Active()&&gameDescription!=null)
                {
                    gameDescription.Text=AppLanguage.T("Описание временно недоступно. Поиск раздач продолжается.");gameDescription.Visibility=Visibility.Visible;
                    if(gameSynopsisSkeleton!=null&&gameSynopsisSkeleton.Parent is Panel skeletonHost){skeletonHost.Children.Remove(gameSynopsisSkeleton);gameSynopsisSkeleton=null;}
                    if(gameAbout!=null&&current!=null)RenderGameAbout(current,new(current,"","","",""));
                }
                ErrorLog.Write(error);
            }
        }
        async Task Releases()
        {
            try
            {
                var progress=new Progress<ReleaseSearchUpdate>(result=>{if(!Active())return;gameReleases=ReleaseSearch.WithSaved(result.Items,gameReleases);gameSourceChecks=result.Sources;RenderGameReleases();});
                var result=await ReleaseSearch.RunAsync((gameSourcesProvider?.Invoke(game)??GameReleaseSources.Create(sourceClient,game,sourceConfigs)),progress,token,TimeSpan.FromSeconds(18));
                if(!Active())return;gameReleases=ReleaseSearch.WithSaved(result.Items,gameReleases);gameSourceChecks=result.Sources;
                if(gameReleases.Length>0){if(gameReleaseCache.Count>=60)gameReleaseCache.Remove(gameReleaseCache.Keys.First());gameReleaseCache[game.Id]=gameReleases;}
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){ErrorLog.Write(error);}
            finally{if(Active()){gameReleasesLoading=false;RenderGameReleases();}}
        }
        try{await Task.WhenAll(Metadata(),Releases());}finally{if(ReferenceEquals(gameDetailRequest,request))gameDetailRequest=null;request.Dispose();}
    }

    // ---------- screenshots ----------
    // One large picture with arrows and a strip of thumbnails; a click moves on, the arrow keys work when the picture has focus.
    void RenderGameGallery(GameDetails? detail)
    {
        if(gameGallery==null)return;
        gameGalleryRequest?.Cancel();gameGalleryRequest=null;gameGallery.Children.Clear();gameShotImage=null;gameShotCounter=null;gameShotStrip=null;gameShotScroll=null;
        gameShots=detail?.Gallery??[];gameShotIndex=0;
        if(prefs.LiteMode||gameShots.Length==0){gameGallery.Visibility=Visibility.Collapsed;return;}
        gameGallery.Visibility=Visibility.Visible;var request=gameGalleryRequest=new CancellationTokenSource();var token=request.Token;
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(2,0,0,14)};
        var title=Text("Скриншоты",22);title.FontWeight=FontWeights.Bold;title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);
        var counter=gameShotCounter=new TextBlock{Name="GameShotCounter",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,Margin=new(10,3,0,0),VerticalAlignment=VerticalAlignment.Center};counter.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");heading.Children.Add(counter);
        gameGallery.Children.Add(heading);

        var stage=new Grid{Name="GameShotStage",Height=400,Focusable=true,FocusVisualStyle=null,Cursor=Cursors.Hand};stage.SetResourceReference(Panel.BackgroundProperty,"PanelAlt");
        var frame=new Border{CornerRadius=new(18),Child=stage};
        frame.SizeChanged+=(_,e)=>
        {
            if(e.NewSize.Width<=0)return;var height=Math.Clamp(e.NewSize.Width*9/16,200,460);if(Math.Abs(stage.Height-height)>1)stage.Height=height;
            var clip=new RectangleGeometry(new Rect(0,0,e.NewSize.Width,Math.Max(1,stage.Height)),18,18);clip.Freeze();stage.Clip=clip;
        };
        var picture=gameShotImage=new Image{Name="GameShotImage",Stretch=Stretch.UniformToFill};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);stage.Children.Add(picture);
        void Step(int direction){if(gameShots.Length>1)SelectGameShot(((gameShotIndex+direction)%gameShots.Length+gameShots.Length)%gameShots.Length,token,true);}
        stage.MouseLeftButtonUp+=(_,_)=>{stage.Focus();Step(1);};
        stage.KeyDown+=(_,e)=>{if(e.Key==Key.Right){Step(1);e.Handled=true;}else if(e.Key==Key.Left){Step(-1);e.Handled=true;}};
        if(gameShots.Length>1)
        {
            Button Arrow(string icon,string name,string tip,HorizontalAlignment side,int direction)
            {
                var button=new Button{Name=name,Style=(Style)FindResource("IconButton"),Width=44,Height=44,Margin=new(14,0,14,0),HorizontalAlignment=side,VerticalAlignment=VerticalAlignment.Center,ToolTip=tip};
                button.SetResourceReference(Control.BackgroundProperty,"PosterBadge");button.Foreground=(Brush)FindResource("BannerText");button.Content=IconLabel("",icon,18);AutomationProperties.SetName(button,AppLanguage.T(tip));
                button.Click+=(_,e)=>{e.Handled=true;stage.Focus();Step(direction);};return button;
            }
            stage.Children.Add(Arrow("IconBack","GameShotPrevious","Предыдущий скриншот",HorizontalAlignment.Left,-1));
            stage.Children.Add(Arrow("IconChevron","GameShotNext","Следующий скриншот",HorizontalAlignment.Right,1));
        }
        gameGallery.Children.Add(frame);

        if(gameShots.Length>1)
        {
            var strip=gameShotStrip=new StackPanel{Name="GameShotStrip",Orientation=Orientation.Horizontal,Margin=new(0,0,0,2)};
            for(var index=0;index<gameShots.Length;index++)
            {
                var number=index;var thumbImage=new Image{Stretch=Stretch.UniformToFill};RenderOptions.SetBitmapScalingMode(thumbImage,BitmapScalingMode.HighQuality);
                var thumb=new Border{Width=160,Height=90,CornerRadius=new(10),BorderThickness=new(2),Child=thumbImage};thumb.SetResourceReference(Border.BackgroundProperty,"PanelAlt");thumb.SizeChanged+=(sender,_)=>ClipPoster((Border)sender);
                var button=new Button{Name="GameShot"+(index+1),Style=(Style)FindResource("PosterButton"),Padding=new(0),Margin=new(0,6,10,10),Content=thumb,Tag=thumb,ToolTip=AppLanguage.T("Скриншот "+(index+1))};
                AutomationProperties.SetName(button,AppLanguage.T("Скриншот "+(index+1)+" из "+gameShots.Length));button.Click+=(_,_)=>{stage.Focus();SelectGameShot(number,token,true);};strip.Children.Add(button);
                _=LoadGameShotImage(thumbImage,gameShots[index].Thumb,320,token,()=>true);
            }
            var scroll=gameShotScroll=new ScrollViewer{Content=strip,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,CanContentScroll=false,Margin=new(-6,8,-6,0),Padding=new(6,0,6,0)};
            scroll.PreviewMouseWheel+=(_,e)=>{if(scroll.ScrollableWidth<=0)return;scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta/120d*120);e.Handled=true;};
            gameGallery.Children.Add(scroll);
        }
        SelectGameShot(0,token,false);
    }
    void SelectGameShot(int index,CancellationToken token,bool reveal)
    {
        if(gameShotImage==null||gameShots.Length==0||token.IsCancellationRequested)return;
        gameShotIndex=Math.Clamp(index,0,gameShots.Length-1);var shot=gameShots[gameShotIndex];var picture=gameShotImage;
        if(gameShotCounter!=null)gameShotCounter.Text=(gameShotIndex+1)+" / "+gameShots.Length;
        if(gameShotStrip!=null)
            for(var position=0;position<gameShotStrip.Children.Count;position++)
                if(gameShotStrip.Children[position] is Button {Tag: Border thumb})
                {
                    var active=position==gameShotIndex;thumb.SetResourceReference(Border.BorderBrushProperty,active?"Accent":"EdgeSoft");thumb.Opacity=active?1:.72;
                    if(active&&reveal&&gameShotScroll!=null&&gameShotStrip.Children[position] is FrameworkElement chosen)chosen.BringIntoView();
                }
        // The thumbnail is already on screen, so it stands in for the large picture until that arrives.
        picture.Source=coverCache.TryGetValue(shot.Thumb+"|320",out var preview)?preview:null;
        var wanted=gameShotIndex;
        _=LoadGameShotImage(picture,shot.Full,1280,token,()=>gameShotIndex==wanted&&ReferenceEquals(gameShotImage,picture));
    }
    async Task LoadGameShotImage(Image image,string url,int decodeWidth,CancellationToken token,Func<bool> wanted)
    {
        var key=url+"|"+decodeWidth;
        if(coverCache.TryGetValue(key,out var cached)){if(wanted())image.Source=cached;return;}
        try
        {
            await coverSlots.WaitAsync(token);
            try
            {
                if(!coverCache.TryGetValue(key,out var bitmap))
                {
                    var path=System.IO.Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");
                    (bitmap,_)=await CoverCache.Load(path,4*1024*1024,ct=>sourceClient.Read(new Uri(url),4*1024*1024,ct),bytes=>
                    {
                        using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=decodeWidth;result.StreamSource=stream;result.EndInit();result.Freeze();return result;
                    },token);
                    token.ThrowIfCancellationRequested();coverCache.Put(key,bitmap);
                }
                if(!closing&&!closed&&wanted()){image.Source=bitmap;FadeInCover(image);}
            }
            finally{coverSlots.Release();}
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){ErrorLog.Write(error);}
    }

    // ---------- releases ----------
    void RenderGameReleases()
    {
        if(gameReleaseHost==null||current?.Section!="Игры")return;
        var game=current;var host=gameReleaseHost;host.Children.Clear();
        var releases=gameReleases;
        if(gameReleaseCount!=null)gameReleaseCount.Text=releases.Length>0?releases.Length.ToString():"";
        RefreshGameActions();
        var failed=gameSourceChecks.Any(x=>x.State is SourceState.TimedOut or SourceState.Unavailable);
        var status=new WrapPanel{Name="GameReleaseStatus",Margin=new(0,0,0,10),VerticalAlignment=VerticalAlignment.Center};host.Children.Add(status);
        var summary=new TextBlock{FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,10)};summary.SetResourceReference(TextBlock.ForegroundProperty,failed&&!gameReleasesLoading?"Warning":"Muted");
        summary.Text=AppLanguage.T(gameReleasesLoading?"Ищем раздачи… · найдено "+releases.Length:failed?"Часть источников недоступна — обнови поиск позже.":releases.Length>0?"Источники ответили":"");
        var now=DateTime.UtcNow;summary.ToolTip=gameSourceChecks.Length==0?null:string.Join("\n",gameSourceChecks.Select(x=>x.Name+": "+ReleaseFreshness.SourceStateText(x,now)));
        if(summary.Text.Length>0)status.Children.Add(summary);
        if(!gameReleasesLoading)
        {
            var refresh=ActionButton("Обновить поиск","IconRefresh",()=>{gameDetailRequest?.Cancel();gameDetailId=null;Render();},"PillButton");refresh.Name="GameReleaseRetry";refresh.Margin=new(0,0,0,10);AutomationProperties.SetName(refresh,AppLanguage.T("Обновить поиск раздач"));status.Children.Add(refresh);
        }
        if(releases.Length==0)
        {
            if(gameReleasesLoading){host.Children.Add(ReleaseSkeleton());return;}
            var empty=new StackPanel{Margin=new(2,6,2,20)};var none=Text("Подходящих раздач пока нет",17);none.FontWeight=FontWeights.SemiBold;empty.Children.Add(none);
            empty.Children.Add(Text("Раздачи ищутся по точному названию игры. Можно обновить поиск или вернуться позже.",13,true));host.Children.Add(empty);return;
        }
        RenderGamePicker(host,game,releases);
    }

    void RenderGamePicker(StackPanel host,MediaItem game,SourceEntry[] releases)
    {
        if(!gamePickerStates.TryGetValue(game.Id,out var state)){if(gamePickerStates.Count>=40)gamePickerStates.Remove(gamePickerStates.Keys.First());state=gamePickerStates[game.Id]=new();}
        var recommended=GameReleaseRecommendation.Pick(releases,prefs.UiLanguage);
        var sorts=(prefs.UiLanguage=="en"?new[]{"По языку интерфейса"}:Array.Empty<string>()).Concat(new[]{"Больше отдающих","Меньше размер","Больше размер","По названию"}).ToArray();
        var sources=releases.Select(x=>x.Source).Distinct().OrderBy(x=>x).ToArray();
        if(state.Source!="Все"&&!sources.Contains(state.Source))state.Source="Все";
        bool compact=Body.ActualWidth<1000;
        // Each render owns one slice with its own SizeChanged handler, so dropping the old slice drops its closures.
        var slice=new StackPanel{Name="GameReleaseSlice"};host.Children.Add(slice);
        var top=new WrapPanel{Name="GameReleaseTop",Margin=new(0,0,0,6),VerticalAlignment=VerticalAlignment.Center};slice.Children.Add(top);
        var results=new StackPanel{Name="GameReleaseList",Margin=new(0,8,0,0)};slice.Children.Add(results);

        Button Chip(string name)
        {
            var chip=new Button{Style=(Style)FindResource("PillButton"),Margin=new(0,0,10,10)};AutomationProperties.SetName(chip,AppLanguage.T(name));top.Children.Add(chip);return chip;
        }
        ToggleContextMenu Menu(Button chip)
        {
            var menu=new ToggleContextMenu{PlacementTarget=chip,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,MaxHeight=380};
            menu.SetResourceReference(Control.BackgroundProperty,"Raised");menu.SetResourceReference(Control.ForegroundProperty,"Text");return menu;
        }
        // Declared before the handlers that call Show(), which reads them.
        Button? sourceChip=null;ToggleContextMenu? sourceMenu=null;Button reset=null!;
        var sortChip=Chip("Сортировка раздач: выбрать");var sortMenu=Menu(sortChip);
        foreach(var name in sorts){var entry=new MenuItem{Header=AppLanguage.T(name),IsCheckable=true,Tag=name};entry.Click+=(_,_)=>{sortMenu.IsOpen=false;state.Sort=name;state.Reverse=false;state.VisibleCount=40;Show();};sortMenu.Items.Add(entry);}
        AttachMenuToggle(sortChip,sortMenu);
        if(sources.Length>1)
        {
            sourceChip=Chip("Источник раздач: выбрать");sourceMenu=Menu(sourceChip);
            foreach(var name in new[]{"Все"}.Concat(sources)){var entry=new MenuItem{Header=AppLanguage.T(name=="Все"?"Все источники":name),IsCheckable=true,Tag=name};entry.Click+=(_,_)=>{sourceMenu.IsOpen=false;state.Source=name;state.VisibleCount=40;Show();};sourceMenu.Items.Add(entry);}
            AttachMenuToggle(sourceChip,sourceMenu);
        }
        reset=new Button{Content=AppLanguage.T("Сбросить"),Style=(Style)FindResource("QuietButton"),Padding=new(12,0,12,0),Margin=new(0,0,10,10)};AutomationProperties.SetName(reset,AppLanguage.T("Сбросить сортировку и источник раздач"));
        reset.Click+=(_,_)=>{state.Sort=sorts[0];state.Source="Все";state.Reverse=false;state.VisibleCount=40;Show();};top.Children.Add(reset);
        void UpdateChips()
        {
            sortChip.Content=ChipContent(state.Sort,true,"IconFilter");StyleChip(sortChip,false);
            foreach(MenuItem entry in sortMenu.Items)entry.IsChecked=Equals(entry.Tag,state.Sort);
            if(sourceChip!=null&&sourceMenu!=null)
            {
                sourceChip.Content=ChipContent(state.Source=="Все"?"Источник":state.Source,true);StyleChip(sourceChip,state.Source!="Все");
                foreach(MenuItem entry in sourceMenu.Items)entry.IsChecked=Equals(entry.Tag,state.Source);
            }
            reset.Visibility=state.Sort!=sorts[0]||state.Source!="Все"||state.Reverse?Visibility.Visible:Visibility.Collapsed;
        }

        IEnumerable<FrameworkElement> Tags(SourceEntry entry,bool full)
        {
            yield return ReleaseTag(GameReleaseInfo.Kind(entry),GameReleaseInfo.Kind(entry)=="Лицензия");
            if(GameReleaseInfo.Group(entry) is {} group)yield return ReleaseTag(group);
            if(GameReleaseInfo.Version(entry) is {} version)yield return ReleaseTag(version);
            if(GameReleaseInfo.Russian(entry))yield return ReleaseTag("Русский язык",true,"Язык определён по названию раздачи.");
            if(GameReleaseInfo.WithDlc(entry))yield return ReleaseTag("С дополнениями");
            if(!full)yield break;
            yield return ReleaseTag(entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"Размер не указан");
            yield return ReleaseTag(entry.Seeds switch{>0=>$"Отдают: {entry.Seeds.Value}",0=>"Отдают: 0 по данным источника",_=>"Отдают: неизвестно"},false,ReleaseFreshness.ConnectionNote);
            if(entry.Leechers.HasValue)yield return ReleaseTag($"Скачивают: {entry.Leechers.Value}",false,ReleaseFreshness.ConnectionNote);
        }
        Grid RowGrid()
        {
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new(){Width=new GridLength(96)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=240});
            grid.ColumnDefinitions.Add(new(){Width=new GridLength(88)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(184)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(120)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(176)});
            return grid;
        }
        FrameworkElement CompactRelease(SourceEntry entry)
        {
            var isBest=recommended!=null&&ReferenceEquals(recommended.Entry,entry);
            var body=new StackPanel();
            if(isBest)body.Children.Add(RecommendedPill());
            var title=TextRaw(entry.Title,14);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,8);body.Children.Add(title);
            var source=Text(ReleaseFreshness.Caption(entry,DateTime.UtcNow),12,true);source.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");source.ToolTip=ReleaseFreshness.Details(entry,DateTime.UtcNow);source.Margin=new(0,0,0,12);body.Children.Add(source);
            var summary=new WrapPanel{Margin=new(0,0,0,6)};foreach(var tag in Tags(entry,true))summary.Children.Add(tag);body.Children.Add(summary);
            if(isBest)body.Children.Add(ReasonLine(recommended!.Reason));
            if(entry.Seeds is >0 and <10)body.Children.Add(LowSeedLine());
            var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,0)};footer.Children.Add(ReleaseAction(entry));body.Children.Add(footer);
            var card=new Border{Child=body,CornerRadius=new(16),Padding=new(16),Margin=new(0,0,0,8),BorderThickness=new(1)};
            card.SetResourceReference(Border.BackgroundProperty,isBest?"AccentFaint":"Panel");card.SetResourceReference(Border.BorderBrushProperty,isBest?"AccentLine":"EdgeSoft");return card;
        }
        // The recommended release on top of the list: larger, with its reason and one primary download button.
        FrameworkElement BestRelease(ReleaseRecommendation.Choice pick)
        {
            var entry=pick.Entry;
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            info.Children.Add(RecommendedPill());
            var title=TextRaw(entry.Title,17);title.FontWeight=FontWeights.SemiBold;title.MaxHeight=48;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=entry.Title;title.Margin=new(0,2,0,10);info.Children.Add(title);
            var tags=new WrapPanel();foreach(var tag in Tags(entry,false))tags.Children.Add(tag);
            if(entry.Size.HasValue)tags.Children.Add(ReleaseTag(DownloadService.FormatBytes(entry.Size.Value)));
            if(entry.Seeds>0)tags.Children.Add(ReleaseTag($"Отдают: {entry.Seeds.Value}",false,ReleaseFreshness.ConnectionNote));
            tags.Children.Add(ReleaseTag(ReleaseFreshness.SourceName(entry)));
            info.Children.Add(tags);info.Children.Add(ReasonLine(pick.Reason));
            grid.Children.Add(info);
            FrameworkElement action;
            if(QueuedDownload(entry)!=null)action=ReleaseAction(entry);
            else
            {
                var button=new Button{Name="GameBestDownload",Style=(Style)FindResource("PrimaryButton"),Tag=new RecommendedDownload(entry),Height=52,MinHeight=52,Padding=new(22,0,22,0),FontSize=15,ToolTip=AppLanguage.T("Скачать рекомендованную раздачу")};
                button.Content=IconLabel("Скачать","IconDownload",18);button.Click+=SourceDownload;
                AutomationProperties.SetName(button,AppLanguage.T("Скачать рекомендованную раздачу: "+entry.Title));action=button;
            }
            action.VerticalAlignment=VerticalAlignment.Center;
            // Narrow windows put the button under the details instead of beside them.
            if(compact){action.HorizontalAlignment=HorizontalAlignment.Left;action.Margin=new(0,14,0,0);Grid.SetRow(action,1);grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetColumnSpan(info,2);}
            else{action.Margin=new(20,0,0,0);Grid.SetColumn(action,1);}
            grid.Children.Add(action);
            var card=new Border{Name="GameBestRelease",Child=grid,CornerRadius=new(20),Padding=new(20,18,20,18),Margin=new(0,0,0,16),BorderThickness=new(1)};
            card.SetResourceReference(Border.BackgroundProperty,"AccentFaint");card.SetResourceReference(Border.BorderBrushProperty,"AccentLine");
            AutomationProperties.SetName(card,AppLanguage.T("Рекомендованная раздача: "+entry.Title));
            return card;
        }
        void Show()
        {
            UpdateChips();results.Children.Clear();
            IEnumerable<SourceEntry> filtered=releases;
            if(state.Source!="Все")filtered=filtered.Where(x=>x.Source==state.Source);
            filtered=state.Sort switch
            {
                "Меньше размер"=>filtered.OrderBy(x=>x.Size??long.MaxValue),
                "Больше размер"=>filtered.OrderByDescending(x=>x.Size??-1),
                "По названию"=>filtered.OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase),
                "По языку интерфейса"=>SearchLanguage.AudioOrder(filtered,prefs.UiLanguage),
                _=>filtered.OrderByDescending(SeedRank)
            };
            if(state.Reverse)filtered=filtered.Reverse();
            var visible=filtered.ToArray();var displayed=visible.Take(state.VisibleCount).ToArray();
            if(visible.Length==0)
            {
                var empty=new StackPanel{Margin=new(20)};
                var title=Text("Таких раздач пока нет",16);title.FontWeight=FontWeights.SemiBold;empty.Children.Add(title);
                empty.Children.Add(Text("Выбери другой источник или сбрось фильтры, чтобы увидеть все варианты.",13,true));
                var surface=new Border{Child=empty,CornerRadius=new(16),BorderThickness=new(1)};surface.SetResourceReference(Border.BackgroundProperty,"Panel");surface.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");results.Children.Add(surface);return;
            }
            if(recommended!=null&&visible.Length>1&&visible.Contains(recommended.Entry))results.Children.Add(BestRelease(recommended));
            void MoreResults()
            {
                if(displayed.Length==visible.Length)return;
                var next=Button($"Показать ещё · осталось {visible.Length-displayed.Length}",()=>{state.VisibleCount+=40;Show();});next.HorizontalAlignment=HorizontalAlignment.Left;next.Margin=new(0,4,0,24);results.Children.Add(next);
            }
            if(compact)
            {
                foreach(var entry in displayed)results.Children.Add(CompactRelease(entry));MoreResults();
                return;
            }
            var header=RowGrid();header.Margin=new(17,0,17,8);
            void Heading(string label,int column,string? mode)
            {
                if(mode==null)
                {
                    var plain=new TextBlock{Text=AppLanguage.T(label),FontSize=11,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};plain.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");Grid.SetColumn(plain,column);header.Children.Add(plain);return;
                }
                var active=state.Sort==mode;
                var descendingByDefault=mode is "Больше отдающих" or "Больше размер";
                var direction=descendingByDefault^(active&&state.Reverse)?" ↓":" ↑";
                var button=new Button{Style=(Style)FindResource("LinkButton"),Content=label+(active?direction:""),FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new(0,6,0,6),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,ToolTip=AppLanguage.T(active?"Нажми ещё раз, чтобы поменять порядок":"Сортировать по этому столбцу")};
                button.Click+=(_,_)=>{if(state.Sort==mode)state.Reverse=!state.Reverse;else{state.Sort=mode;state.Reverse=false;}state.VisibleCount=40;Show();};
                button.SetResourceReference(Control.ForegroundProperty,active?"Accent":"Subtle");AutomationProperties.SetName(button,AppLanguage.T("Сортировать раздачи: "+mode));
                Grid.SetColumn(button,column);header.Children.Add(button);
            }
            Heading("ТИП",0,null);Heading("РАЗДАЧА",1,"По названию");Heading("РАЗМЕР",2,"Меньше размер");Heading("ОТДАЮТ · КАЧАЮТ",3,"Больше отдающих");Heading("ДОБАВЛЕНА",4,null);
            results.Children.Add(header);
            foreach(var entry in displayed)
            {
                var isBest=recommended!=null&&ReferenceEquals(recommended.Entry,entry);
                var row=RowGrid();
                var kind=new TextBlock{Text=AppLanguage.T(GameReleaseInfo.Kind(entry)),FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0),TextTrimming=TextTrimming.CharacterEllipsis};row.Children.Add(kind);
                var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};Grid.SetColumn(identity,1);
                var titleLine=new DockPanel();if(isBest){var pill=RecommendedPill();pill.Margin=new(0,0,10,0);DockPanel.SetDock(pill,Dock.Left);titleLine.Children.Add(pill);}
                var title=new TextBlock{Text=entry.Title,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=entry.Title,VerticalAlignment=VerticalAlignment.Center};titleLine.Children.Add(title);identity.Children.Add(titleLine);
                var tags=new WrapPanel{Margin=new(0,6,0,0)};foreach(var tag in Tags(entry,false).Skip(1))tags.Children.Add(tag);
                if(tags.Children.Count>0)identity.Children.Add(tags);
                if(isBest)identity.Children.Add(ReasonLine(recommended!.Reason));
                if(entry.Seeds is >0 and <10)identity.Children.Add(LowSeedLine());
                row.Children.Add(identity);
                var size=new TextBlock{Text=entry.Size.HasValue?DownloadService.FormatBytes(entry.Size.Value):"—",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(size,2);row.Children.Add(size);
                var health=SeedHealth(entry);Grid.SetColumn(health,3);row.Children.Add(health);
                var age=new StackPanel{VerticalAlignment=VerticalAlignment.Center,ToolTip=ReleaseFreshness.Details(entry,DateTime.UtcNow)};Grid.SetColumn(age,4);
                var when=new TextBlock{Text=AppLanguage.T(ReleaseFreshness.Age(entry.DataReceivedUtc,DateTime.UtcNow)),FontSize=13};when.SetResourceReference(TextBlock.ForegroundProperty,"Muted");age.Children.Add(when);
                var origin=new TextBlock{Text=AppLanguage.T(ReleaseFreshness.SourceName(entry)+(ReleaseFreshness.FromIndex(entry)?" · индекс":"")),FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,2,6,0)};origin.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");age.Children.Add(origin);row.Children.Add(age);
                var download=ReleaseAction(entry);download.HorizontalAlignment=HorizontalAlignment.Right;download.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(download,5);row.Children.Add(download);
                var rowFrame=new Border{Child=row,CornerRadius=new(16),BorderThickness=new(1),Padding=new(16,12,16,12),Margin=new(0,0,0,8)};
                rowFrame.SetResourceReference(Border.BackgroundProperty,isBest?"AccentFaint":"Panel");rowFrame.SetResourceReference(Border.BorderBrushProperty,isBest?"AccentLine":"EdgeSoft");results.Children.Add(rowFrame);
            }
            MoreResults();
        }
        // A window resized across the compact breakpoint relays the whole list out.
        slice.SizeChanged+=(_,_)=>{var next=Body.ActualWidth<1000;if(next==compact)return;compact=next;Show();};
        Show();
    }
}
