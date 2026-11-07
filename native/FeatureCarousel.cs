using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Kachalka;

// One hero banner that shows up to five featured titles. Switching a slide replaces the bound item. As in the macOS
// version the slides change by themselves every eight seconds (HeroRotation), with a short fade, unless motion is
// off; a pause button, the pointer, the keyboard focus, an inactive window or a scrolled-away banner stop the change.
public partial class MainWindow
{
    sealed class FeatureCarouselView
    {
        public required MediaItem[] Slides;
        public int Index;
        public required Border Frame;
        public required Image Poster;
        public required TextBlock Eyebrow,Title,People,Summary;
        public Func<MediaItem,int,string>? EyebrowText;
        public DispatcherTimer? Clock;
        public bool Hovered,Paused;
        public Button? Pause;
        public required Grid SummarySlot;
        public CancellationTokenSource? SummaryRequest;
        public required WrapPanel Meta;
        public required StackPanel Dots;
        public required Button About,Favorite,Previous,Next;
        public required Border LiteCover,PosterHost;
        public MediaItem? Bound;
        public PropertyChangedEventHandler? Watch;
    }
    FeatureCarouselView? carousel;
    bool scrollToReleasesAfterOpen;

    // eyebrowText: the line above the title, given the slide and its position; without it the usual "in the spotlight" wording is used.
    FrameworkElement BuildFeatureCarousel(MediaItem[] slides,Func<MediaItem,int,string>? eyebrowText=null)
    {
        slides=slides.Take(5).ToArray();
        // Square and edge to edge, as the banner of the macOS version; its height follows the window (ArrangeFeatureCarousel).
        var frame=new Border{Name="FeatureFrame",CornerRadius=new(0),Height=HeroHeight(1280,double.NaN)};
        frame.Background=(Brush)FindResource("BannerBase");
        var grid=new Grid{Name="FeatureArtwork"};frame.Child=grid;
        grid.ClipToBounds=true;
        var image=new Image{Width=0,Height=0,Opacity=0,Tag="FeaturePoster"};
        // Lite mode paints no backdrop, so it does not request the large feature poster at all.
        if(!prefs.LiteMode){image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;}
        grid.Children.Add(image);
        // Only lite mode keeps a separate portrait. Normal banners reuse their
        // ordinary decoded poster as a tiny preblurred, stretched background.
        var liteCover=new Border{Width=96,Height=144,CornerRadius=new(10),ClipToBounds=true,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,36,0),Visibility=Visibility.Visible};
        var liteImage=new Image{Stretch=Stretch.UniformToFill};liteImage.Loaded+=SourceCover;liteImage.DataContextChanged+=SourceCoverChanged;liteCover.Tag=liteImage;
        if(prefs.LiteMode)liteCover.Child=liteImage;
        else
        {
            liteCover.Visibility=Visibility.Collapsed;
            liteImage.Width=liteImage.Height=0;liteImage.Opacity=0;liteImage.Tag="FeaturePlaceholderPoster";grid.Children.Add(liteImage);
            var onlyMissing=new Style(typeof(Image));onlyMissing.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));
            var missing=new DataTrigger{Binding=new Binding("Source"){Source=image},Value=null};missing.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible));onlyMissing.Triggers.Add(missing);liteImage.Style=onlyMissing;
            AddBannerArtwork(grid,image,liteImage,hero:true);
        }
        var posterHost=new Border{Child=liteCover};grid.Children.Add(posterHost);

        var content=new StackPanel{Name="FeatureContent",VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=620,Margin=new(94,0,0,58)};grid.Children.Add(content);
        var eyebrowRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,10)};
        var sparkle=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconSparkle"),Width=14,Height=14,Stretch=Stretch.Uniform,Fill=Brushes.White,Opacity=.9,Margin=new(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};eyebrowRow.Children.Add(sparkle);
        var eyebrow=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,Opacity=.9,VerticalAlignment=VerticalAlignment.Center};eyebrowRow.Children.Add(eyebrow);content.Children.Add(eyebrowRow);
        var title=new TextBlock{Name="FeatureTitle",FontSize=48,FontWeight=FontWeights.Bold,LineHeight=52,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=104,Margin=new(0,0,0,10)};content.Children.Add(title);
        var meta=new WrapPanel{Margin=new(0,0,0,8)};content.Children.Add(meta);
        var summarySlot=new Grid{Name="FeatureSummarySlot",Height=60,Margin=new(0,0,0,14),Visibility=Visibility.Collapsed};content.Children.Add(summarySlot);
        var summary=new TextBlock{Name="FeatureSummary",FontSize=15,LineHeight=20,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brushes.White,Opacity=.9};summarySlot.Children.Add(summary);
        var people=new TextBlock{FontSize=14,Foreground=(Brush)FindResource("BannerMuted"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,0,18)};content.Children.Add(people);
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new(0,10,0,0)};content.Children.Add(actions);
        Button Action(string text,string icon,bool primary)
        {
            // The main action is the white pill of the macOS banner (black text); the others stay translucent.
            var button=new Button{Style=(Style)FindResource(primary?"HeroPrimaryButton":typeof(Button)),Height=44,MinHeight=44,Margin=new(0,0,10,0),Padding=new(24,0,24,0),FontSize=15,FontWeight=FontWeights.SemiBold};
            if(!primary){button.SetResourceReference(Control.BackgroundProperty,"BannerAction");button.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");button.Foreground=(Brush)FindResource("BannerText");}
            button.Content=IconLabel(text,icon,18);AutomationProperties.SetName(button,AppLanguage.T(text));return button;
        }
        var aboutAction=Action("Подробнее","IconInfo",true);
        var favorite=new Button{Style=(Style)FindResource("RoundIconButton"),Width=44,Height=44,Margin=new(0)};favorite.Foreground=Brushes.White;
        actions.Children.Add(aboutAction);actions.Children.Add(favorite);

        var dots=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,0,14)};grid.Children.Add(dots);
        Button Arrow(string icon,string name)
        {
            var button=new Button{Style=(Style)FindResource("RoundIconButton"),Width=40,Height=40,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=icon=="IconChevronLeft"?HorizontalAlignment.Left:HorizontalAlignment.Right,Margin=new(44,0,44,0)};button.Foreground=Brushes.White;
            button.Content=IconLabel("",icon,16);AutomationProperties.SetName(button,AppLanguage.T(name));button.ToolTip=name;return button;
        }
        var games=section=="Игры";
        var previous=Arrow("IconChevronLeft",games?"Предыдущая игра":"Предыдущий фильм");var next=Arrow("IconChevron",games?"Следующая игра":"Следующий фильм");grid.Children.Add(previous);grid.Children.Add(next);
        // The pause button of the slideshow, at the right end of the row of dots; it exists only while the slides change by themselves.
        var pause=new Button{Style=(Style)FindResource("RoundIconButton"),Width=40,Height=40,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,50,8)};pause.Foreground=Brushes.White;
        grid.Children.Add(pause);
        if(slides.Length<2){dots.Visibility=Visibility.Collapsed;previous.Visibility=Visibility.Collapsed;next.Visibility=Visibility.Collapsed;}
        if(slides.Length<2||!Motion)pause.Visibility=Visibility.Collapsed;

        var view=new FeatureCarouselView{Slides=slides,Frame=frame,Poster=image,Eyebrow=eyebrow,Title=title,People=people,Summary=summary,SummarySlot=summarySlot,Meta=meta,Dots=dots,About=aboutAction,Favorite=favorite,Previous=previous,Next=next,LiteCover=liteCover,PosterHost=posterHost,Pause=pause};
        frame.ContextMenu=new ContextMenu();frame.ContextMenuOpening+=(_,_)=>{if(view.Bound!=null)frame.ContextMenu=CatalogPersonalMenu(view.Bound);};
        view.EyebrowText=eyebrowText;carousel=view;
        previous.Click+=(_,_)=>ShowFeatureSlide(view,(view.Index+view.Slides.Length-1)%view.Slides.Length,true);
        next.Click+=(_,_)=>ShowFeatureSlide(view,(view.Index+1)%view.Slides.Length,true);
        UpdateFeaturePause(view);
        pause.Click+=(_,_)=>{view.Paused=!view.Paused;UpdateFeaturePause(view);RestartFeatureClock(view);};
        frame.MouseEnter+=(_,_)=>view.Hovered=true;frame.MouseLeave+=(_,_)=>view.Hovered=false;
        view.Clock=new DispatcherTimer{Interval=HeroRotation.Interval};
        view.Clock.Tick+=(_,_)=>{if(FeatureAutoplayReady(view))ShowFeatureSlide(view,(view.Index+1)%view.Slides.Length,true);};
        aboutAction.Click+=OpenCard;
        favorite.Click+=(_,_)=>
        {
            if(view.Bound==null)return;ToggleSaved(view.Bound);UpdateFeatureFavorite(view);UpdateSavedCount();
        };
        frame.Unloaded+=(_,_)=>{DetachFeatureWatch(view);view.Clock?.Stop();view.Hovered=false;};
        frame.Loaded+=(_,_)=>{if(view.Bound is {} bound){if(view.Watch==null)ShowFeatureSlide(view,view.Index);else StartFeatureSummary(view,bound);}RestartFeatureClock(view);};
        ShowFeatureSlide(view,0);
        return frame;
    }
    void DetachFeatureWatch(FeatureCarouselView view)
    {
        var request=view.SummaryRequest;view.SummaryRequest=null;request?.Cancel();
        if(view.Bound!=null&&view.Watch!=null)view.Bound.PropertyChanged-=view.Watch;view.Watch=null;
    }
    // animate: a change the viewer or the clock asked for fades in; the first showing and a reload do not.
    void ShowFeatureSlide(FeatureCarouselView view,int index,bool animate=false)
    {
        if(view.Slides.Length==0)return;
        if(animate&&Motion&&view.Frame.Child is Grid faded)faded.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.55,1,TimeSpan.FromMilliseconds(380)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
        index=Math.Clamp(index,0,view.Slides.Length-1);view.Index=index;
        var item=view.Slides[index];
        DetachFeatureWatch(view);view.Bound=item;
        view.Poster.DataContext=item;if(view.LiteCover.Tag is Image lite)lite.DataContext=item;
        view.Eyebrow.Text=view.EyebrowText?.Invoke(item,index)??AppLanguage.T(index==0?"В центре внимания":item.Section=="Игры"?"Стоит поиграть":"Стоит посмотреть");
        view.Title.Text=item.DisplayTitle;view.Title.ToolTip=item.DisplayTitle;
        view.About.Tag=item;view.Frame.Tag=item;
        AutomationProperties.SetName(view.About,AppLanguage.T("Подробнее: "+item.DisplayTitle));
        RebuildFeatureMeta(view,item);UpdateFeaturePeople(view,item);UpdateFeatureFavorite(view);UpdateFeatureDots(view);
        view.Watch=(_,e)=>{if(e.PropertyName is null or nameof(MediaItem.DisplayTitle) or nameof(MediaItem.DisplayDescription) or nameof(MediaItem.DisplayPeople)){view.Title.Text=item.DisplayTitle;view.Title.ToolTip=item.DisplayTitle;UpdateFeaturePeople(view,item);UpdateFeatureSummary(view,item);}
            if(e.PropertyName is null or nameof(MediaItem.PosterQuality)or nameof(MediaItem.CardRating)or nameof(MediaItem.CardGenre)){RebuildFeatureMeta(view,item);}};
        item.PropertyChanged+=view.Watch;
        StartFeatureSummary(view,item);
        RestartFeatureClock(view);
    }
    // The clock starts again with every slide, so a slide the viewer chose stays for the whole interval.
    void RestartFeatureClock(FeatureCarouselView view)
    {
        if(view.Clock==null)return;
        view.Clock.Stop();
        if(view.Frame.IsLoaded&&view.Slides.Length>1&&Motion)view.Clock.Start();
    }
    bool FeatureAutoplayReady(FeatureCarouselView view)=>
        HeroRotation.CanAdvance(view.Slides.Length,FeatureBannerVisible(view),IsActive,view.Hovered||view.Frame.IsKeyboardFocusWithin,view.Paused,!Motion);
    // On screen: shown, and at least a third of it (or a good strip) inside the scrolling page.
    static bool FeatureBannerVisible(FeatureCarouselView view)
    {
        var frame=view.Frame;if(!frame.IsVisible)return false;
        DependencyObject? parent=VisualTreeHelper.GetParent(frame);
        while(parent!=null&&parent is not ScrollViewer)parent=VisualTreeHelper.GetParent(parent);
        if(parent is not ScrollViewer scroll)return true;
        try
        {
            var bounds=frame.TransformToAncestor(scroll).TransformBounds(new Rect(frame.RenderSize));
            var seen=Rect.Intersect(bounds,new Rect(0,0,scroll.ViewportWidth,scroll.ViewportHeight));
            return !seen.IsEmpty&&seen.Height>=Math.Min(bounds.Height/3,120);
        }
        catch(InvalidOperationException){return false;}
    }
    void UpdateFeaturePause(FeatureCarouselView view)
    {
        if(view.Pause==null)return;
        view.Pause.Content=IconLabel("",view.Paused?"IconPlay":"IconPause",16);
        var name=view.Paused?"Продолжить смену баннеров":"Приостановить смену баннеров";
        view.Pause.ToolTip=AppLanguage.T(name);AutomationProperties.SetName(view.Pause,AppLanguage.T(name));
    }
    void RebuildFeatureMeta(FeatureCarouselView view,MediaItem item)
    {
        view.Meta.Children.Clear();
        TextBlock Label(string text,bool bold=false)
        {
            var label=new TextBlock{Text=AppLanguage.T(text),FontSize=14,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,Foreground=Brushes.White,Opacity=.85,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,4)};return label;
        }
        Border Badge(string text)=>new(){BorderBrush=(Brush)FindResource("BannerBadgeEdge"),BorderThickness=new(1),CornerRadius=new(6),Padding=new(6,2,6,2),Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=AppLanguage.T(text),FontSize=11,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText")}};
        if(item.Section=="Игры")
        {
            // The list row already knows the year, genre, price and reviews; the store details add the score, the studio and the exact price.
            gameDetails.TryGetValue(item.Id,out var game);var info=game?.Item??item;var store=item.Store;
            int percent=0,count=0;var tone="";var verdict="";
            if(game?.Review is {} reviewed){percent=reviewed.Percent;count=reviewed.Count;tone=reviewed.Tone;verdict=reviewed.Label;}
            else if(store is {HasReview:true}){percent=store.ReviewPercent;count=store.ReviewCount;tone=store.ReviewTone;verdict=store.ReviewLabel;}
            if(percent>0&&tone.Length>0)view.Meta.Children.Add(GameReviewChip(percent,tone,verdict,count));
            if(game is {Metacritic:>0})view.Meta.Children.Add(new TextBlock{Text="Metacritic "+game.Metacritic,FontSize=14,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,4),ToolTip=AppLanguage.T("Оценка критиков на Metacritic")});
            view.Meta.Children.Add(Label("Игра"));
            if(store is {Soon:true,Released.Length:>0})view.Meta.Children.Add(Label("Выйдет "+store.SoonLabel));
            else if(info.Year>0)view.Meta.Children.Add(Label(info.Year.ToString()));
            if(info.Genre.Length>0&&info.Genre!="Windows")view.Meta.Children.Add(Label(info.CardGenre));
            view.Meta.Children.Add(Badge("Windows"));
            if(game is {Price.Length:>0})view.Meta.Children.Add(GamePriceBadge(game.Price,game.Discount,game.OriginalPrice));
            else if(store is {HasPrice:true})view.Meta.Children.Add(GamePriceBadge(store.Price,store.Discount,store.OriginalPrice));
            return;
        }
        // Each rating carries the mark of its site; the genre stands where the word «Фильм» used to be.
        foreach(var (source,value) in MediaRatings(item)){var rating=RatingRow(source,value,14,(Brush)FindResource("BannerText"));rating.Margin=new(0,0,12,4);view.Meta.Children.Add(rating);}
        var genres=string.Join(", ",item.AllGenres.Split(", ",StringSplitOptions.RemoveEmptyEntries).Take(2));
        var facts=string.Join(" · ",new[]{item.Year>0?item.Year.ToString():"",genres}.Where(x=>x.Length>0));
        if(facts.Length>0)view.Meta.Children.Add(Label(facts));
        var qualities=item.ReleaseResolutions.OrderByDescending(x=>x).Select(x=>x>=2160?"4K":x+"p").Take(3).ToList();
        if(qualities.Count==0&&item.PosterQuality.Length>0)qualities.Add(item.PosterQuality);
        foreach(var quality in qualities)
        {
            var badge=new Border{Background=(Brush)FindResource("HeroBadgeFill"),CornerRadius=new(7),Padding=new(7,4,7,4),Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=AppLanguage.T(quality),FontSize=11,FontWeight=FontWeights.Bold,Foreground=Brushes.White}};
            view.Meta.Children.Add(badge);
        }
    }
    void UpdateFeaturePeople(FeatureCarouselView view,MediaItem item)
    {
        if(item.Section=="Игры")
        {
            // A game's studio comes with its details, which the games home loads for the banner slides.
            var studio=gameDetails.TryGetValue(item.Id,out var game)?game.Developers:"";
            view.People.Text=AppLanguage.T(studio.Length>0?"Разработчик "+studio:"");view.People.Visibility=studio.Length>0&&!featureCompactText?Visibility.Visible:Visibility.Collapsed;return;
        }
        // The macOS banner shows no cast line for films and series, and neither does this one: the title, the facts and the summary are enough.
        view.People.Visibility=Visibility.Collapsed;
    }
    void UpdateFeatureFavorite(FeatureCarouselView view)
    {
        var saved=view.Bound!=null&&IsSaved(view.Bound);
        view.Favorite.Content=IconLabel("",saved?"IconHeartFilled":"IconHeart",20);
        var name=saved?"Сохранено":"Сохранить";view.Favorite.ToolTip=name;AutomationProperties.SetName(view.Favorite,AppLanguage.T(name));
    }
    void UpdateFeatureDots(FeatureCarouselView view)
    {
        view.Dots.Children.Clear();
        for(var index=0;index<view.Slides.Length;index++)
        {
            var active=index==view.Index;var target=index;
            var dot=new Border{Width=30,Height=28,Cursor=System.Windows.Input.Cursors.Hand,Background=Brushes.Transparent};
            dot.Child=new Border{Width=active?26:8,Height=8,CornerRadius=new(4),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=active?Brushes.White:(Brush)FindResource("HeroDotOff")};
            AutomationProperties.SetName(dot,AppLanguage.T(view.Slides[index].DisplayTitle));
            dot.MouseLeftButtonUp+=(_,_)=>ShowFeatureSlide(view,target,true);
            view.Dots.Children.Add(dot);
        }
    }
    bool featureCompactText;
    // The height of the catalog banner in the macOS version: about half the width, between 380 and 560, and never taller
    // than the window leaves room for (the actions stay in view in a short window).
    static double HeroHeight(double width,double viewport)
    {
        var landscape=Math.Min(560,Math.Max(380,width*.48));
        return double.IsNaN(viewport)?landscape:Math.Min(landscape,Math.Max(280,viewport-40));
    }
    // Called with the body width. The banner runs edge to edge; its text keeps the column of the page below it.
    void ArrangeFeatureCarousel(double width)
    {
        if(carousel is not {} view)return;
        var gutter=CenterRegion.Margin.Left;
        var frameWidth=view.Frame.ActualWidth>0?view.Frame.ActualWidth:width+2*gutter;
        var height=HeroHeight(frameWidth,Body.ActualHeight>0?Body.ActualHeight:double.NaN);
        view.Frame.Height=height;
        var column=frameWidth-2*gutter;var compact=column<680;var shortBanner=height<340;
        view.Title.FontSize=shortBanner?26:compact?32:48;view.Title.LineHeight=view.Title.FontSize+4;view.Title.MaxHeight=view.Title.LineHeight*2;
        featureCompactText=compact;
        // The lite-mode portrait stays clear of the arrow and of the text in narrow layouts; a game's wide capsule has no portrait to show.
        view.PosterHost.Visibility=prefs.LiteMode&&width>=860&&section!="Игры"?Visibility.Visible:Visibility.Collapsed;
        view.LiteCover.Margin=new(0,0,gutter+64,0);
        var single=view.Slides.Length<2;
        if(view.Frame.Child is Grid grid&&grid.Children.OfType<StackPanel>().FirstOrDefault(x=>x.Name=="FeatureContent") is {} content)
        {
            content.MaxWidth=Math.Max(120,Math.Min(column-124,620));
            content.Margin=new(gutter+62,0,0,single?28:58);
        }
        view.Previous.Margin=new(gutter+12,0,0,0);view.Next.Margin=new(0,0,gutter+12,0);
        if(view.Pause!=null)view.Pause.Margin=new(0,0,gutter+18,8);
        view.About.Padding=new(compact?18:24,0,compact?18:24,0);
    }
}
