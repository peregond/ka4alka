using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Kachalka;

// Film and series card: a banner header with the poster, identity, credits, a short
// description and actions that depend on what the queue already knows about the title.
public partial class MainWindow
{
    ScrollViewer? detailScroll;
    Panel? detailActions;
    TextBlock? releaseCountLabel;
    StackPanel? releaseControlsHost;
    string detailActionState="";
    static readonly string[] VideoExtensions=[".mkv",".mp4",".avi",".mov",".m4v",".wmv",".ts",".webm",".mpg",".mpeg"];

    void RenderLiveDetail(MediaItem item)
    {
        if(requestedDetails.Add(item.Id))
        {
            var cached=catalogIndex.CachedReleaseSnapshot(item);
            if(cached is {Items.Length:>0}&&!liveReleases.ContainsKey(item.Id))
            {
                liveReleases[item.Id]=cached.Items;cachedReleaseViews.Add(item.Id);
                releaseViews[item.Id]=new(){Saved=true,SavedUtc=cached.SavedUtc,Sources=cached.Sources??[]};
            }
            _ = FetchDetails(item);
        }
        ApplyKnownQuality(item);
        if(descriptionItemId!=item.Id){descriptionItemId=item.Id;descriptionExpanded=false;}
        SetBack(returnPerson?.Person.Name??SavedBackLabel()??DownloadBackLabel()??section,CinemaBack);
        // The scroll viewer extends 6 px past the gutter so focus rings are not clipped.
        var panel=new StackPanel{Margin=new(6,6,6,0)};
        detailScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=panel,Margin=new(-6,-6,-6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Body.Children.Add(detailScroll);

        // ---- banner ----
        var heroFrame=new Border{Name="CinemaFilm",CornerRadius=new(24),VerticalAlignment=VerticalAlignment.Top};heroFrame.Background=(Brush)FindResource("BannerBase");
        var artwork=new Grid{Name="DetailArtwork"};heroFrame.Child=artwork;
        artwork.SizeChanged+=(_,e)=>{if(e.NewSize.Width<=0||e.NewSize.Height<=0)return;var clip=new RectangleGeometry(new Rect(e.NewSize),24,24);clip.Freeze();artwork.Clip=clip;};
        var backdropImage=new Image{DataContext=item,Width=0,Height=0,Opacity=0,Tag="FeaturePoster"};if(!prefs.LiteMode){backdropImage.Loaded+=SourceCover;backdropImage.DataContextChanged+=SourceCoverChanged;}artwork.Children.Add(backdropImage);
        var image=new Image{DataContext=item,Stretch=Stretch.UniformToFill};image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;
        if(!prefs.LiteMode)AddBannerArtwork(artwork,backdropImage,image);
        detailHero=new Grid{Name="DetailHero",Margin=new(32,28,32,28)};
        detailHero.ColumnDefinitions.Add(new(){Width=GridLength.Auto});detailHero.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        detailHero.RowDefinitions.Add(new(){Height=GridLength.Auto});
        artwork.Children.Add(detailHero);

        var posterGrid=new Grid();
        posterGrid.Children.Add(new TextBlock{Text="Постер\nнедоступен",Foreground=(Brush)FindResource("BannerMuted"),Opacity=.75,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=11});
        posterGrid.Children.Add(image);
        detailPoster=new Border{Width=168,Height=252,CornerRadius=new(14),ClipToBounds=true,Background=item.Cover,BorderBrush=(Brush)FindResource("BannerChip"),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,0,28,0),Child=posterGrid};
        detailPoster.SizeChanged+=(sender,_)=>ClipPoster((Border)sender);
        detailHero.Children.Add(detailPoster);

        detailInfo=new StackPanel{Name="DetailIdentity",VerticalAlignment=VerticalAlignment.Bottom};detailInfo.SizeChanged+=(sender,_)=>{if(ReferenceEquals(sender,detailInfo))UpdateDetailLayout();};Grid.SetColumn(detailInfo,1);detailHero.Children.Add(detailInfo);

        // A quiet caption instead of pills: "ФИЛЬМ · 2026", in the same voice as the sidebar section labels.
        var caption=new StackPanel{Name="DetailCaption",Orientation=Orientation.Horizontal,Margin=new(0,0,0,12)};
        TextBlock CaptionText(string text,string? name=null)
        {
            var label=new TextBlock{Text=text,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center};if(name!=null)label.Name=name;return label;
        }
        caption.Children.Add(CaptionText(item.Section=="Сериалы"?"СЕРИАЛ":"ФИЛЬМ"));
        if(item.Year>0){caption.Children.Add(CaptionText("  ·  "));caption.Children.Add(CaptionText(item.Year.ToString(),"DetailYear"));}
        detailInfo.Children.Add(caption);

        var hasOriginal=!string.IsNullOrWhiteSpace(item.OriginalTitle)&&!item.OriginalTitle.Equals(item.Title,StringComparison.OrdinalIgnoreCase);
        detailTitle=new TextBlock{Name="DetailTitle",Text=item.Title,FontSize=56,FontWeight=FontWeights.ExtraBold,LineHeight=58,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("BannerText"),Margin=new(0,0,0,hasOriginal?4:12),ToolTip=item.Title};
        detailInfo.Children.Add(detailTitle);
        if(hasOriginal)
        {
            var original=new TextBlock{Name="DetailOriginalTitle",Text=item.OriginalTitle,FontSize=15,Foreground=(Brush)FindResource("BannerMuted"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};detailInfo.Children.Add(original);
        }
        // rating, genres and available quality
        detailMetaRow=new WrapPanel{Name="DetailMetaRow",Margin=new(0,0,0,10)};detailInfo.Children.Add(detailMetaRow);
        detailRatings=new WrapPanel{Name="DetailRatings",Margin=new(0,0,12,4)};detailMetaRow.Children.Add(detailRatings);
        void Rating(string name,string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value=="—")return;
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,14,0),ToolTip=name+" · "+value};
            var star=new TextBlock{Text="★",FontSize=14,Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};star.SetResourceReference(TextBlock.ForegroundProperty,"RatingInk");row.Children.Add(star);
            row.Children.Add(new TextBlock{Text=value,FontSize=14,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center});
            row.Children.Add(new TextBlock{Text=" "+name,FontSize=12,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center});
            detailRatings.Children.Add(row);
        }
        Rating("Кинопоиск",item.Kinopoisk);Rating("IMDb",item.Imdb);
        var traits=string.Join(" · ",new[]{item.Genre,item.Country}.Where(x=>!string.IsNullOrWhiteSpace(x)));
        if(traits.Length>0)detailMetaRow.Children.Add(new TextBlock{Name="DetailTraits",Text=traits,FontSize=14,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,4)});
        foreach(var quality in item.ReleaseResolutions.OrderByDescending(x=>x).Select(x=>x>=2160?"4K":x+"p").Take(4))
            detailMetaRow.Children.Add(new Border{BorderBrush=(Brush)FindResource("BannerBadgeEdge"),BorderThickness=new(1),CornerRadius=new(6),Padding=new(6,2,6,2),Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=quality,FontSize=11,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText")}});
        if(item.OnlyPoorQuality){var poor=PoorQualityBadge(item.PosterQuality);poor.Foreground=(Brush)FindResource("BannerText");detailInfo.Children.Add(poor);}
        // credits, only those already known
        var directors=item.People.Where(x=>x.Role=="Режиссёры").Take(2).ToArray();
        var cast=item.People.Where(x=>x.Role=="Актёры").Take(5).ToArray();
        if(directors.Length+cast.Length>0)
        {
            var credits=new Grid{Name="DetailCredits",Margin=new(0,0,0,12)};credits.ColumnDefinitions.Add(new(){Width=GridLength.Auto});credits.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            void Credit(string label,CinemaPerson[] people)
            {
                if(people.Length==0)return;var row=credits.RowDefinitions.Count;credits.RowDefinitions.Add(new(){Height=GridLength.Auto});
                var caption=new TextBlock{Text=label,FontSize=14,Foreground=(Brush)FindResource("BannerMuted"),Margin=new(0,0,16,4)};credits.Children.Add(caption);Grid.SetRow(caption,row);
                // Every name opens that person's page, the same as the participant tiles below the banner.
                var names=new WrapPanel{Margin=new(0,0,0,4)};Grid.SetColumn(names,1);Grid.SetRow(names,row);credits.Children.Add(names);
                for(var index=0;index<people.Length;index++)
                {
                    var person=people[index];
                    var link=new Button{Name="DetailCreditLink",Style=(Style)FindResource("LinkButton"),Content=person.Name,FontSize=14,Foreground=(Brush)FindResource("BannerText"),Tag=person,ToolTip="Открыть страницу: "+person.Name,VerticalAlignment=VerticalAlignment.Center};
                    AutomationProperties.SetName(link,"Открыть страницу: "+person.Name+", "+label);link.Click+=(_,_)=>OpenPerson(person,item);names.Children.Add(link);
                    if(index<people.Length-1)names.Children.Add(new TextBlock{Text=", ",FontSize=14,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,5,0)});
                }
            }
            Credit("Режиссёр",directors);Credit("В ролях",cast);detailInfo.Children.Add(credits);
        }
        // description
        detailDescription=new StackPanel{Name="DetailDescription",Margin=new(0,0,0,16)};detailInfo.Children.Add(detailDescription);
        detailSynopsis=new TextBlock{Name="DetailSynopsis",Text=DetailDescriptionText(item),FontSize=15,LineHeight=23,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=(Brush)FindResource("BannerMuted"),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=descriptionExpanded?double.PositiveInfinity:69};
        detailDescription.Children.Add(detailSynopsis);
        descriptionToggle=Button(descriptionExpanded?"Свернуть описание":"Читать дальше",()=>
        {
            if(descriptionItemId!=item.Id||detailSynopsis==null)return;
            descriptionExpanded=!descriptionExpanded;detailSynopsis.MaxHeight=descriptionExpanded?double.PositiveInfinity:69;UpdateDescriptionToggle();
        });descriptionToggle.Name="DescriptionToggle";descriptionToggle.Style=(Style)FindResource("QuietButton");descriptionToggle.Foreground=(Brush)FindResource("BannerText");descriptionToggle.HorizontalAlignment=HorizontalAlignment.Left;descriptionToggle.Padding=new(0,5,0,5);descriptionToggle.Margin=new(0);descriptionToggle.MinHeight=28;descriptionToggle.Visibility=Visibility.Collapsed;detailDescription.Children.Add(descriptionToggle);
        if(DetailMetadataNeedsRetry(item.Id)&&!MediaMetadata.HasDescription(item)){var retry=ActionButton("Повторить загрузку карточки","IconRefresh",()=>RetryDetailMetadata(item));retry.Name="DetailMetadataRetry";retry.HorizontalAlignment=HorizontalAlignment.Left;retry.Margin=new(0,8,0,0);detailDescription.Children.Add(retry);}
        detailSynopsis.SizeChanged+=(sender,_)=>{if(ReferenceEquals(sender,detailSynopsis))UpdateDescriptionToggle();};
        // actions follow the state of the title
        detailActions=new WrapPanel{Name="DetailActions",Margin=new(0,0,0,-8)};detailInfo.Children.Add(detailActions);
        liveReleases.TryGetValue(item.Id,out var known);
        BuildDetailActions(item,known??[]);

        var cards=new Grid{Name="CinemaCards",Margin=new(0,0,0,28)};cards.RowDefinitions.Add(new(){Height=GridLength.Auto});cards.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.Children.Add(cards);cards.Children.Add(heroFrame);
        var participants=RenderCinemaConnections(panel,item,cards);
        // The participant card sits below the banner at every width, using the full content width.
        Grid.SetRow(participants,1);Grid.SetColumn(participants,0);participants.Margin=new(0,20,0,0);
        UpdateDetailLayout();

        // ---- releases ----
        var titleRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,16)};
        var releasesTitle=Text("Раздачи",22);releasesTitle.FontWeight=FontWeights.Bold;releasesTitle.Margin=new(0);releasesTitle.VerticalAlignment=VerticalAlignment.Center;titleRow.Children.Add(releasesTitle);
        releaseCountLabel=new TextBlock{Name="ReleaseCount",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,Margin=new(10,3,0,0),VerticalAlignment=VerticalAlignment.Center};releaseCountLabel.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");titleRow.Children.Add(releaseCountLabel);
        panel.Children.Add(titleRow);
        releaseControlsHost=new StackPanel{Name="ReleaseControls"};panel.Children.Add(releaseControlsHost);
        RenderReleaseToolbar(panel,item);
        if(!liveReleases.TryGetValue(item.Id,out var releases)||releases.Count==0)
        {
            releaseCountLabel.Text="";
            var checking=releaseViews.TryGetValue(item.Id,out var scan)&&scan.Checking;
            if(checking){FinishDetailRender();return;}
            var empty=new StackPanel{Margin=new(2,16,2,20)};var noReleases=Text("Подходящих раздач пока нет",17);noReleases.FontWeight=FontWeights.SemiBold;empty.Children.Add(noReleases);empty.Children.Add(Text("Можно обновить поиск раздач или вернуться позже.",13,true));
            panel.Children.Add(empty);FinishDetailRender();return;
        }
        RenderReleasePicker(panel,releases);
        FinishDetailRender();
    }
    void FinishDetailRender()
    {
        if(!scrollToReleasesAfterOpen)return;
        scrollToReleasesAfterOpen=false;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(ScrollToReleases));
    }
    void ScrollToReleases()
    {
        if(detailScroll==null||detailScroll.Content is not FrameworkElement content)return;
        var target=FindVisual<WrapPanel>(content,x=>x.Name=="ReleaseToolbar")??(FrameworkElement?)releaseControlsHost;
        if(target==null||!target.IsLoaded)return;
        UpdateLayout();
        var offset=target.TransformToAncestor(content).Transform(new Point()).Y-86;
        detailScroll.ScrollToVerticalOffset(Math.Max(0,offset));
    }

    // ---------- state dependent actions ----------
    DownloadItem[] DownloadsOf(MediaItem item)
    {
        var key=PageKey(item.PageUrl);if(key.Length==0)return [];
        return downloads.Items.Where(x=>PageKey(x.MediaPageUrl)==key).ToArray();
    }
    string DetailActionState(MediaItem item,IReadOnlyList<SourceEntry> releases)
    {
        var owned=DownloadsOf(item);
        if(owned.Any(x=>x.Completed))return "done:"+owned.First(x=>x.Completed).Id;
        var active=owned.FirstOrDefault(x=>!x.Completed);
        if(active!=null)return "active:"+active.Id;
        var pick=ReleaseRecommendation.Pick(releases,QualityMinimum);
        return pick==null?"none":"pick:"+pick.Entry.Id+"|"+releases.Count;
    }
    void RefreshDetailActions()
    {
        if(current==null||detailActions==null||section is "Загрузки" or "Настройки"||activePerson!=null)return;
        liveReleases.TryGetValue(current.Id,out var known);
        var state=DetailActionState(current,known??[]);
        if(state==detailActionState)return;
        BuildDetailActions(current,known??[]);
    }
    void BuildDetailActions(MediaItem item,IReadOnlyList<SourceEntry> releases)
    {
        if(detailActions==null)return;
        detailActions.Children.Clear();detailActionState=DetailActionState(item,releases);
        var owned=DownloadsOf(item);var done=owned.FirstOrDefault(x=>x.Completed);var active=owned.FirstOrDefault(x=>!x.Completed);
        FrameworkElement Spaced(FrameworkElement element){element.Margin=new(0,0,10,8);return element;}
        Button Action(string text,string icon,bool primary,Action click)
        {
            var button=new Button{Style=(Style)FindResource(primary?"PrimaryButton":typeof(Button)),Height=52,MinHeight=52,Padding=new(22,0,22,0),FontSize=15,Margin=new(0,0,10,8)};
            if(!primary){button.SetResourceReference(Control.BackgroundProperty,"BannerAction");button.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");button.Foreground=(Brush)FindResource("BannerText");}
            button.Content=IconLabel(text,icon,18);AutomationProperties.SetName(button,text);button.Click+=(_,_)=>click();return button;
        }
        if(done!=null)
        {
            detailActions.Children.Add(Action("Смотреть","IconPlay",true,()=>WatchDownload(done)));
            detailActions.Children.Add(Action("Показать в папке","IconFolder",false,()=>ShowDownloadInFolder(done)));
        }
        else if(active!=null)
        {
            detailActions.Children.Add(Spaced(DownloadProgressButton(active)));
        }
        else if(ReleaseRecommendation.Pick(releases,QualityMinimum) is {} pick)
        {
            var button=new Button{Name="DetailDownload",Style=(Style)FindResource("PrimaryButton"),Tag=pick.Entry,Height=52,MinHeight=52,Padding=new(22,0,22,0),FontSize=15,Margin=new(0,0,10,8),ToolTip="Скачать рекомендованную раздачу: "+pick.Entry.Title};
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            row.Children.Add(IconLabel("Скачать","IconDownload",18));
            var detail=string.Join(" · ",new[]{(ReleaseQuality.Height(pick.Entry) is int h?h+"p":pick.Entry.Quality),pick.Entry.Size.HasValue?DownloadService.FormatBytes(pick.Entry.Size.Value):""}.Where(x=>x.Length>0));
            row.Children.Add(new TextBlock{Text=detail,FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,FontWeight=FontWeights.Medium,Opacity=.75,Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            button.Content=row;AutomationProperties.SetName(button,"Скачать раздачу: "+pick.Entry.Title);button.Click+=SourceDownload;detailActions.Children.Add(button);
        }
        else
        {
            detailActions.Children.Add(Action("Выбрать раздачу","IconDownload",true,()=>ScrollToReleases()));
        }
        // No separate "all releases" button: the list sits right below the banner on this page.
        var saved=IsSaved(item);
        Button favorite=null!;favorite=new Button{Name="DetailFavorite",Style=(Style)FindResource("IconButton"),Width=52,Height=52,Margin=new(0,0,10,8)};
        favorite.SetResourceReference(Control.BackgroundProperty,"BannerAction");favorite.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");favorite.Foreground=(Brush)FindResource("BannerText");
        void Paint(){var now=IsSaved(item);favorite.Content=IconLabel("",now?"IconHeartFilled":"IconHeart",20);favorite.ToolTip=now?"Сохранено":"Сохранить";AutomationProperties.SetName(favorite,now?"Сохранено":"Сохранить");}
        Paint();favorite.Click+=(_,_)=>{ToggleSaved(item);Paint();UpdateSavedCount();};
        detailActions.Children.Add(favorite);
    }
    FrameworkElement DownloadProgressButton(DownloadItem item)
    {
        var frame=new Border{Name="DetailProgress",Height=52,MinWidth=200,CornerRadius=new(12),BorderThickness=new(1),Cursor=System.Windows.Input.Cursors.Hand,DataContext=item,ToolTip="Открыть загрузки",Background=Brushes.Transparent};
        frame.SetResourceReference(Border.BorderBrushProperty,"AccentLine");
        var inner=new Grid();frame.Child=inner;
        inner.SizeChanged+=(_,e)=>{if(e.NewSize.Width<=0||e.NewSize.Height<=0)return;var clip=new RectangleGeometry(new Rect(e.NewSize),11,11);clip.Freeze();inner.Clip=clip;};
        var track=new ProgressBar{Minimum=0,Maximum=100,BorderThickness=new(0),IsHitTestVisible=false};track.SetResourceReference(Control.BackgroundProperty,"Panel");track.SetResourceReference(Control.ForegroundProperty,"AccentSoft");
        track.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new Binding("Progress"){Mode=BindingMode.OneWay});inner.Children.Add(track);
        var row=new Grid{Margin=new(20,0,20,0),IsHitTestVisible=false};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});inner.Children.Add(row);
        var left=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var glyph=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconDownload"),Width=18,Height=18,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(0,0,10,0)};glyph.SetResourceReference(Shape.StrokeProperty,"Accent");left.Children.Add(glyph);
        var text=new TextBlock{FontSize=15,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};text.SetBinding(TextBlock.TextProperty,new Binding("Progress"){StringFormat="Качается · {0:F0}%"});text.SetResourceReference(TextBlock.ForegroundProperty,"Text");left.Children.Add(text);row.Children.Add(left);
        var eta=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=13,Margin=new(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};eta.SetBinding(TextBlock.TextProperty,new Binding("Remaining"));eta.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(eta,1);row.Children.Add(eta);
        frame.MouseLeftButtonUp+=(_,_)=>{searchDelay.Stop();section="Загрузки";current=null;activePerson=null;downloadReturnItem=item;Render();};
        frame.Focusable=true;AutomationProperties.SetName(frame,"Открыть загрузку");
        return frame;
    }
    void WatchDownload(DownloadItem item)
    {
        try
        {
            // The largest video in the torrent is the film; an ambiguous result opens the folder instead.
            var videos=item.Files.Where(x=>VideoExtensions.Contains(System.IO.Path.GetExtension(x.Name),StringComparer.OrdinalIgnoreCase)&&File.Exists(x.FullPath)).OrderByDescending(x=>x.Size).ToArray();
            var unique=videos.Length==1||videos.Length>1&&videos[0].Size>videos[1].Size*2||item.MediaSection=="Фильмы"&&videos.Length>0;
            if(unique){Process.Start(new ProcessStartInfo(videos[0].FullPath){UseShellExecute=true});return;}
            ShowDownloadInFolder(item);
        }
        catch(Exception error){Status.Text="Не удалось открыть файл: "+error.Message;}
    }
    void ShowDownloadInFolder(DownloadItem item)
    {
        try
        {
            var file=item.Files.OrderByDescending(x=>x.Size).FirstOrDefault(x=>File.Exists(x.FullPath));
            if(file!=null){Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+file.FullPath+"\"")); return;}
            if(Directory.Exists(item.Folder))Process.Start(new ProcessStartInfo(item.Folder){UseShellExecute=true});
        }
        catch(Exception error){Status.Text="Не удалось открыть папку: "+error.Message;}
    }

    // ---------- responsive layout ----------
    void UpdateDetailLayout()
    {
        if(detailHero==null||detailPoster==null||detailTitle==null||detailDescription==null||detailInfo==null)return;
        var width=Body.ActualWidth>0?Body.ActualWidth:ActualWidth;
        var narrow=width<760;var tiny=width<520;
        var posterWidth=tiny?132d:narrow?120d:168d;detailPoster.Width=posterWidth;detailPoster.Height=posterWidth*1.5;
        detailHero.Margin=tiny?new(20,20,20,22):narrow?new(24,24,24,24):new(32,28,32,28);
        detailPoster.Margin=tiny?new(0,0,0,18):new(0,0,narrow?20:28,0);
        // On a very narrow banner the poster moves above the text.
        Grid.SetColumn(detailInfo,tiny?0:1);Grid.SetRow(detailInfo,tiny?1:0);Grid.SetColumnSpan(detailInfo,tiny?2:1);
        if(tiny&&detailHero.RowDefinitions.Count<2)detailHero.RowDefinitions.Add(new(){Height=GridLength.Auto});
        if(!tiny&&detailHero.RowDefinitions.Count>1)detailHero.RowDefinitions.RemoveAt(1);
        Grid.SetColumnSpan(detailPoster,tiny?2:1);detailPoster.HorizontalAlignment=HorizontalAlignment.Left;
        detailTitle.FontSize=tiny?30:narrow?40:56;detailTitle.LineHeight=tiny?33:narrow?44:58;
    }
    void UpdateDescriptionToggle()
    {
        if(detailSynopsis==null||descriptionToggle==null||detailSynopsis.ActualWidth<=0)return;
        descriptionToggle.Content=descriptionExpanded?"Свернуть описание":"Читать дальше";
        var formatted=new FormattedText(detailSynopsis.Text,System.Globalization.CultureInfo.CurrentCulture,detailSynopsis.FlowDirection,new Typeface(detailSynopsis.FontFamily,detailSynopsis.FontStyle,detailSynopsis.FontWeight,detailSynopsis.FontStretch),detailSynopsis.FontSize,detailSynopsis.Foreground,null,TextOptions.GetTextFormattingMode(detailSynopsis),VisualTreeHelper.GetDpi(detailSynopsis).PixelsPerDip){MaxTextWidth=detailSynopsis.ActualWidth,LineHeight=detailSynopsis.LineHeight};
        descriptionToggle.Visibility=descriptionExpanded||formatted.Height>detailSynopsis.LineHeight*3+.5?Visibility.Visible:Visibility.Collapsed;
    }
}
