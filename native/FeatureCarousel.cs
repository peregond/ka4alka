using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Kachalka;

// One hero banner that shows up to five featured titles. Switching a slide only
// replaces the bound item: there is no autoplay, timer or transition animation.
public partial class MainWindow
{
    sealed class FeatureCarouselView
    {
        public required MediaItem[] Slides;
        public int Index;
        public required Border Frame;
        public required Image Poster;
        public required TextBlock Eyebrow,Title,People;
        public required WrapPanel Meta;
        public required StackPanel Dots;
        public required Button Primary,About,Favorite,Previous,Next;
        public required Border LiteCover;
        public MediaItem? Bound;
        public PropertyChangedEventHandler? Watch;
    }
    FeatureCarouselView? carousel;
    bool scrollToReleasesAfterOpen;

    FrameworkElement BuildFeatureCarousel(MediaItem[] slides)
    {
        slides=slides.Take(5).ToArray();
        var frame=new Border{Name="FeatureFrame",CornerRadius=new(22),MinHeight=260};
        frame.Background=(Brush)FindResource("BannerBase");
        var grid=new Grid{Name="FeatureArtwork"};frame.Child=grid;
        grid.SizeChanged+=(_,e)=>
        {
            if(e.NewSize.Width<=0||e.NewSize.Height<=0)return;
            var clip=new RectangleGeometry(new Rect(e.NewSize),22,22);clip.Freeze();grid.Clip=clip;
        };
        var image=new Image{Width=0,Height=0,Opacity=0,Tag="FeaturePoster"};
        // Lite mode paints no backdrop, so it does not request the large feature poster at all.
        if(!prefs.LiteMode){image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;}
        grid.Children.Add(image);
        var backdrop=new Border();
        if(!prefs.LiteMode)
        {
            var picture=new ImageBrush{Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Top};
            BindingOperations.SetBinding(picture,ImageBrush.ImageSourceProperty,new Binding("Source"){Source=image});
            backdrop.Background=picture;
        }
        grid.Children.Add(backdrop);
        if(!prefs.LiteMode)
        {
            grid.Children.Add(new Border{Background=(Brush)FindResource("ScrimHorizontal"),IsHitTestVisible=false});
            grid.Children.Add(new Border{Background=(Brush)FindResource("ScrimVertical"),IsHitTestVisible=false});
        }
        // Light mode keeps the flat Panel surface and shows the small poster instead of a backdrop.
        var liteCover=new Border{Width=96,Height=144,CornerRadius=new(10),ClipToBounds=true,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,36,0),Visibility=prefs.LiteMode?Visibility.Visible:Visibility.Collapsed};
        if(prefs.LiteMode){var liteImage=new Image{Stretch=Stretch.UniformToFill};liteImage.Loaded+=SourceCover;liteImage.DataContextChanged+=SourceCoverChanged;liteCover.Child=liteImage;liteCover.Tag=liteImage;}
        grid.Children.Add(liteCover);

        var content=new StackPanel{Name="FeatureContent",VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=660,Margin=new(36,32,36,32)};grid.Children.Add(content);
        var eyebrowRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,14)};
        var dot=new Ellipse{Width=6,Height=6,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Shape.FillProperty,"Accent");eyebrowRow.Children.Add(dot);
        var eyebrow=new TextBlock{FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center};eyebrowRow.Children.Add(eyebrow);content.Children.Add(eyebrowRow);
        var title=new TextBlock{Name="FeatureTitle",FontSize=52,FontWeight=FontWeights.ExtraBold,LineHeight=54,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=(Brush)FindResource("BannerText"),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=108,Margin=new(0,0,0,14)};content.Children.Add(title);
        var meta=new WrapPanel{Margin=new(0,0,0,10)};content.Children.Add(meta);
        var people=new TextBlock{FontSize=14,Foreground=(Brush)FindResource("BannerMuted"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,0,18)};content.Children.Add(people);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,4,0,0)};content.Children.Add(actions);
        Button Action(string text,string icon,bool primary)
        {
            var button=new Button{Style=(Style)FindResource(primary?"PrimaryButton":typeof(Button)),Height=52,MinHeight=52,Margin=new(0,0,10,0),Padding=new(22,0,22,0),FontSize=15};
            if(!primary){button.SetResourceReference(Control.BackgroundProperty,"BannerAction");button.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");button.Foreground=(Brush)FindResource("BannerText");}
            button.Content=IconLabel(text,icon,18);AutomationProperties.SetName(button,text);return button;
        }
        var primaryAction=Action("Выбрать раздачу","IconDownload",true);var aboutAction=Action("О фильме","IconInfo",false);
        var favorite=new Button{Style=(Style)FindResource("IconButton"),Width=52,Height=52,Margin=new(0)};favorite.SetResourceReference(Control.BackgroundProperty,"BannerAction");favorite.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");favorite.Foreground=(Brush)FindResource("BannerText");
        actions.Children.Add(primaryAction);actions.Children.Add(aboutAction);actions.Children.Add(favorite);

        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,20,20,0)};grid.Children.Add(controls);
        var dots=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,14,0)};controls.Children.Add(dots);
        Button Arrow(string icon,string name)
        {
            var button=new Button{Style=(Style)FindResource("RoundIconButton"),Width=40,Height=40,Margin=new(6,0,0,0)};button.Foreground=(Brush)FindResource("BannerText");
            button.Content=IconLabel("",icon,16);AutomationProperties.SetName(button,name);button.ToolTip=name;return button;
        }
        var previous=Arrow("IconChevronLeft","Предыдущий фильм");var next=Arrow("IconChevron","Следующий фильм");controls.Children.Add(previous);controls.Children.Add(next);
        if(slides.Length<2){dots.Visibility=Visibility.Collapsed;previous.Visibility=Visibility.Collapsed;next.Visibility=Visibility.Collapsed;}

        var view=new FeatureCarouselView{Slides=slides,Frame=frame,Poster=image,Eyebrow=eyebrow,Title=title,People=people,Meta=meta,Dots=dots,Primary=primaryAction,About=aboutAction,Favorite=favorite,Previous=previous,Next=next,LiteCover=liteCover};
        carousel=view;
        previous.Click+=(_,_)=>ShowFeatureSlide(view,(view.Index+view.Slides.Length-1)%view.Slides.Length);
        next.Click+=(_,_)=>ShowFeatureSlide(view,(view.Index+1)%view.Slides.Length);
        primaryAction.Click+=(_,_)=>{scrollToReleasesAfterOpen=true;OpenCard(primaryAction,new RoutedEventArgs());};
        aboutAction.Click+=OpenCard;
        favorite.Click+=(_,_)=>
        {
            if(view.Bound==null)return;ToggleSaved(view.Bound);UpdateFeatureFavorite(view);UpdateSavedCount();
        };
        frame.Unloaded+=(_,_)=>DetachFeatureWatch(view);
        frame.Loaded+=(_,_)=>{if(view.Watch==null&&view.Bound!=null)ShowFeatureSlide(view,view.Index);};
        ShowFeatureSlide(view,0);
        return frame;
    }
    void DetachFeatureWatch(FeatureCarouselView view)
    {
        if(view.Bound!=null&&view.Watch!=null)view.Bound.PropertyChanged-=view.Watch;view.Watch=null;
    }
    void ShowFeatureSlide(FeatureCarouselView view,int index)
    {
        if(view.Slides.Length==0)return;
        index=Math.Clamp(index,0,view.Slides.Length-1);view.Index=index;
        var item=view.Slides[index];
        DetachFeatureWatch(view);view.Bound=item;
        view.Poster.DataContext=item;if(view.LiteCover.Tag is Image lite)lite.DataContext=item;
        view.Eyebrow.Text=index==0?"В центре внимания":"Стоит посмотреть";
        view.Title.Text=item.Title;view.Title.ToolTip=item.Title;
        view.Primary.Tag=item;view.About.Tag=item;view.Frame.Tag=item;
        AutomationProperties.SetName(view.Primary,"Выбрать раздачу: "+item.Title);AutomationProperties.SetName(view.About,"О фильме: "+item.Title);
        RebuildFeatureMeta(view,item);UpdateFeaturePeople(view,item);UpdateFeatureFavorite(view);UpdateFeatureDots(view);
        view.Watch=(_,e)=>{if(e.PropertyName is null or nameof(MediaItem.PosterQuality)or nameof(MediaItem.CardRating)or nameof(MediaItem.CardGenre)){RebuildFeatureMeta(view,item);}};
        item.PropertyChanged+=view.Watch;
    }
    void RebuildFeatureMeta(FeatureCarouselView view,MediaItem item)
    {
        view.Meta.Children.Clear();
        TextBlock Label(string text,bool bold=false)
        {
            var label=new TextBlock{Text=text,FontSize=14,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,Foreground=(Brush)FindResource("BannerMuted"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,4)};return label;
        }
        if(item.HasCardRating)
        {
            var rating=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,12,4)};
            var star=new TextBlock{Text="★",FontSize=14,Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};star.SetResourceReference(TextBlock.ForegroundProperty,"RatingInk");rating.Children.Add(star);
            rating.Children.Add(new TextBlock{Text=item.CardRating,FontSize=14,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText"),VerticalAlignment=VerticalAlignment.Center});view.Meta.Children.Add(rating);
        }
        if(item.Year>0)view.Meta.Children.Add(Label(item.Year.ToString()));
        view.Meta.Children.Add(Label(item.Section=="Сериалы"?"Сериал":"Фильм"));
        if(item.CardGenre.Length>0&&item.Genre.Length>0)view.Meta.Children.Add(Label(item.CardGenre));
        var qualities=item.ReleaseResolutions.OrderByDescending(x=>x).Select(x=>x>=2160?"4K":x+"p").Take(3).ToList();
        if(qualities.Count==0&&item.PosterQuality.Length>0)qualities.Add(item.PosterQuality);
        foreach(var quality in qualities)
        {
            var badge=new Border{BorderBrush=(Brush)FindResource("BannerBadgeEdge"),BorderThickness=new(1),CornerRadius=new(6),Padding=new(6,2,6,2),Margin=new(0,0,8,4),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=quality,FontSize=11,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("BannerText")}};
            view.Meta.Children.Add(badge);
        }
    }
    void UpdateFeaturePeople(FeatureCarouselView view,MediaItem item)
    {
        // Only credits that the item already carries; no extra requests are made for the banner.
        var director=item.People.Where(x=>x.Role=="Режиссёры").Select(x=>x.Name).Take(1).ToArray();
        var cast=item.People.Where(x=>x.Role=="Актёры").Select(x=>x.Name).Take(2).ToArray();
        var parts=new List<string>();
        if(director.Length>0)parts.Add("Режиссёр "+director[0]);
        if(cast.Length>0)parts.Add("В ролях "+string.Join(", ",cast));
        view.People.Text=string.Join(" · ",parts);view.People.Visibility=parts.Count>0&&!featureCompactText?Visibility.Visible:Visibility.Collapsed;
    }
    void UpdateFeatureFavorite(FeatureCarouselView view)
    {
        var saved=view.Bound!=null&&IsSaved(view.Bound);
        view.Favorite.Content=IconLabel("",saved?"IconHeartFilled":"IconHeart",20);
        var name=saved?"Сохранено":"Сохранить";view.Favorite.ToolTip=name;AutomationProperties.SetName(view.Favorite,name);
    }
    void UpdateFeatureDots(FeatureCarouselView view)
    {
        view.Dots.Children.Clear();
        for(var index=0;index<view.Slides.Length;index++)
        {
            var active=index==view.Index;var target=index;
            var dot=new Border{Width=active?24:8,Height=8,CornerRadius=new(4),Margin=new(index==0?0:6,0,0,0),Cursor=System.Windows.Input.Cursors.Hand,Background=Brushes.Transparent};
            var fill=new Border{CornerRadius=new(4)};if(active)fill.SetResourceReference(Border.BackgroundProperty,"Accent");else fill.Background=(Brush)FindResource("BannerDot");dot.Child=fill;
            dot.MouseLeftButtonUp+=(_,_)=>ShowFeatureSlide(view,target);
            view.Dots.Children.Add(dot);
        }
    }
    bool featureCompactText;
    // Called with the body width; the banner has no fixed height, only the title and secondary details adapt.
    void ArrangeFeatureCarousel(double width)
    {
        if(carousel is not {} view)return;
        var narrow=width<760;var tiny=width<520;
        view.Title.FontSize=tiny?28:narrow?36:52;view.Title.LineHeight=tiny?30:narrow?38:54;view.Title.MaxHeight=view.Title.LineHeight*2;
        featureCompactText=narrow;
        if(view.Bound!=null)UpdateFeaturePeople(view,view.Bound);
        view.About.Visibility=tiny?Visibility.Collapsed:Visibility.Visible;
        // The Lite cover sits on the right; below this width the text column would run over it.
        view.LiteCover.Visibility=prefs.LiteMode&&width>=860?Visibility.Visible:Visibility.Collapsed;
        if(view.Frame.Child is Grid grid&&grid.Children.OfType<StackPanel>().FirstOrDefault(x=>x.Name=="FeatureContent") is {} content)content.Margin=tiny?new(20,24,20,22):narrow?new(24,28,24,26):new(36,32,36,32);
        view.Primary.Padding=new(tiny?16:22,0,tiny?16:22,0);
    }
}
