using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Kachalka;

// "Актуальное" reads like a news front page: a masthead with the date, a top story, a digest of three stories, then
// rubrics that each have their own shape (a lead story with a list, a poster shelf, a numbered ranking).
// Everything is built from the cards the page already has; nothing here requests anything extra.
public partial class MainWindow
{
    readonly HashSet<int> featuredUsed=[],featuredHeroIds=[];
    string? featuredHeroKey,featuredDigestKey;
    Grid? featuredHero;
    StackPanel? featuredDigest;
    double featuredWidth;

    // Wednesday, 14 October: today's date in the interface language.
    static string FeaturedDateLine()
    {
        var culture=CultureInfo.GetCultureInfo(AppLanguage.Current=="en"?"en-US":"ru-RU");
        var text=DateTime.Now.ToString("dddd, d MMMM",culture);
        return char.ToUpper(text[0],culture)+text[1..];
    }
    static string FeaturedKey(IEnumerable<MediaItem> items)=>string.Join(",",items.Select(item=>item.Id))+"|"+AppLanguage.Current;

    // Clickable story: one button around its content, opening the normal card like every poster in the app.
    Button StoryButton(MediaItem item,UIElement content,string label)
    {
        var button=new Button{Style=(Style)FindResource("PosterButton"),Padding=new(0),Margin=new(0),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,Tag=item,Content=content,ContextMenu=new ContextMenu()};
        button.Click+=OpenCard;button.ContextMenuOpening+=CatalogPosterMenu;AutomationProperties.SetName(button,AppLanguage.T("Открыть")+" "+label);return button;
    }
    // The poster in a story or a list row. Width is fixed when given; otherwise it follows its column like a shelf poster.
    FrameworkElement StoryPoster(MediaItem item,double? width=null,double radius=12)
    {
        var art=new Grid();
        var image=new Image{DataContext=item,Stretch=Stretch.UniformToFill};image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;art.Children.Add(image);
        art.Children.Add(new Border{Name="PosterShimmer",CornerRadius=new(radius),IsHitTestVisible=false,Visibility=Visibility.Collapsed});
        var poster=new Border{Background=item.Cover,CornerRadius=new(radius),ClipToBounds=true,Child=art,VerticalAlignment=VerticalAlignment.Top};
        if(width is {} fixedWidth){poster.Width=fixedWidth;poster.Height=fixedWidth*1.5;poster.SizeChanged+=(_,_)=>ClipPoster(poster);}
        else poster.SizeChanged+=PosterResized;
        return poster;
    }
    FrameworkElement RatingMeta(MediaItem item,double size=12)
    {
        var line=new StackPanel{Orientation=Orientation.Horizontal};
        if(item.HasCardRating)
        {
            var star=new TextBlock{Text="★",FontSize=size,Margin=new(0,0,4,0)};star.SetResourceReference(TextBlock.ForegroundProperty,"RatingInk");line.Children.Add(star);
            var rating=new TextBlock{Text=item.CardRating,FontSize=size,FontWeight=FontWeights.Bold,Margin=new(0,0,6,0)};rating.SetResourceReference(TextBlock.ForegroundProperty,"RatingInk");line.Children.Add(rating);
        }
        var rest=string.Join(" · ",new[]{item.Year>0?item.Year.ToString():"",item.CardGenre}.Where(part=>part.Length>0));
        var tail=new TextBlock{Text=rest,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis};tail.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");line.Children.Add(tail);
        return line;
    }
    // Section label above a block that is not a shelf.
    TextBlock Rubric(string text)
    {
        var rubric=TextRaw(text.ToUpperInvariant(),11);rubric.FontWeight=FontWeights.Bold;rubric.Margin=new(0,0,0,6);rubric.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return rubric;
    }

    // ── Top story ──
    // The first title of each rubric rotates in the big banner; the line above the title names the rubric.
    void BuildFeaturedHero(FeaturedGroup[] groups)
    {
        if(featuredHero==null)return;
        var slides=groups.Where(group=>!group.Collection.IsGames&&!group.Collection.Id.StartsWith("award-",StringComparison.Ordinal)).Select(group=>(Group:group,Item:FeaturedVisible(group).FirstOrDefault())).Where(row=>row.Item!=null).DistinctBy(row=>row.Item!.Id).Take(5).ToArray();
        var key=FeaturedKey(slides.Select(row=>row.Item!));
        if(key==featuredHeroKey)return;featuredHeroKey=key;
        featuredHero.Children.Clear();featuredHeroIds.Clear();featuredDigestKey=null;
        if(slides.Length==0){carousel=null;featuredHero.Margin=new(0);return;}
        var rubrics=slides.ToDictionary(row=>row.Item!.Id,row=>row.Group.Collection.Label);
        featuredHero.Margin=new(0,0,0,32);
        featuredHero.Children.Add(BuildFeatureCarousel(slides.Select(row=>row.Item!).ToArray(),(item,_)=>AppLanguage.T("Главное")+" · "+rubrics[item.Id]));
        foreach(var row in slides)featuredHeroIds.Add(row.Item!.Id);
        if(featuredWidth>0)ArrangeFeatureCarousel(featuredWidth);
    }

    // ── Three stories ──
    // A digest of titles that are not in the banner: rubric, headline, a few lines of text and the rating.
    int featuredDigestColumns;
    static int DigestColumns(double width)=>width<760?1:width<1100?2:3;
    void BuildFeaturedDigest(FeaturedGroup[] groups)
    {
        if(featuredDigest==null)return;
        featuredDigestColumns=DigestColumns(featuredWidth);
        var picks=groups.Where(group=>!group.Collection.IsGames&&!group.Collection.Id.StartsWith("award-",StringComparison.Ordinal)).Select(group=>(Group:group,Item:FeaturedVisible(group).Where(item=>!featuredHeroIds.Contains(item.Id)).OrderBy(item=>string.IsNullOrWhiteSpace(item.DisplayDescription)?1:0).FirstOrDefault())).Where(row=>row.Item!=null).DistinctBy(row=>row.Item!.Id).Take(3).ToArray();
        // Titles already told above (banner, digest) go last in the lead story's list below.
        featuredUsed.Clear();featuredUsed.UnionWith(featuredHeroIds);foreach(var row in picks)featuredUsed.Add(row.Item!.Id);
        var key=FeaturedKey(picks.Select(row=>row.Item!))+"|"+(featuredWidth<760?1:featuredWidth<1100?2:3);
        if(key==featuredDigestKey)return;featuredDigestKey=key;
        featuredDigest.Children.Clear();
        if(picks.Length==0){featuredDigest.Margin=new(0);return;}
        featuredDigest.Margin=new(0,0,0,34);
        var heading=Text("Главное сегодня",22);heading.FontWeight=FontWeights.Bold;heading.Margin=new(0,0,0,4);featuredDigest.Children.Add(heading);
        var deck=Text("Три истории, с которых стоит начать",13,true);deck.Margin=new(0,0,0,14);featuredDigest.Children.Add(deck);
        var columns=Math.Min(picks.Length,featuredWidth<760?1:featuredWidth<1100?2:3);
        var grid=new UniformGrid{Columns=columns,Margin=new(0,0,-16,0)};
        foreach(var (group,item) in picks)
        {
            var text=new StackPanel{Margin=new(14,0,0,0)};
            var kicker=TextRaw(group.Collection.Label.ToUpperInvariant(),10);kicker.FontWeight=FontWeights.Bold;kicker.Margin=new(0,2,0,4);kicker.TextTrimming=TextTrimming.CharacterEllipsis;kicker.TextWrapping=TextWrapping.NoWrap;kicker.SetResourceReference(TextBlock.ForegroundProperty,"Accent");text.Children.Add(kicker);
            var title=TextRaw(item.DisplayTitle,16);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,0,0,6);title.MaxHeight=42;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);
            var summary=TextRaw(item.DisplayDescription??"",12.5,true);summary.LineHeight=18;summary.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;summary.MaxHeight=54;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.Margin=new(0,0,0,8);if(summary.Text.Length>0)text.Children.Add(summary);
            text.Children.Add(RatingMeta(item));
            var body=new Grid();body.ColumnDefinitions.Add(new(){Width=GridLength.Auto});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            body.Children.Add(StoryPoster(item,92,10));Grid.SetColumn(text,1);body.Children.Add(text);
            var card=new Border{Background=null,CornerRadius=new(16),BorderThickness=new(1),Padding=new(12),Child=StoryButton(item,body,item.DisplayTitle),Margin=new(0,0,16,16)};
            card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
            grid.Children.Add(card);
        }
        featuredDigest.Children.Add(grid);
    }

    // ── A lead story with a list beside it ──
    // The first title not yet shown above becomes the story, with its text; the next ones form a numbered list.
    void FillFeaturedLead(StackPanel host,MediaItem[] items,int columns)
    {
        var key=FeaturedKey(items)+"|"+columns+"|"+string.Join(",",items.Where(item=>featuredUsed.Contains(item.Id)).Select(item=>item.Id));
        if(host.Tag as string==key&&host.Children.Count>0)return;host.Tag=key;
        host.Children.Clear();
        if(items.Length==0)return;
        var ordered=items.OrderBy(item=>featuredUsed.Contains(item.Id)?1:0).ToArray();
        var lead=ordered[0];var rest=ordered.Skip(1).Take(4).ToArray();
        var wide=columns>=5&&rest.Length>0;
        var grid=new Grid{Margin=new(0,4,0,0)};
        grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.15,GridUnitType.Star)});
        if(wide){grid.ColumnDefinitions.Add(new(){Width=new GridLength(26)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});}
        var story=new Grid();story.ColumnDefinitions.Add(new(){Width=GridLength.Auto});story.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        story.Children.Add(StoryPoster(lead,columns>=5?190:150,14));
        var text=new StackPanel{Margin=new(20,2,0,0)};
        text.Children.Add(RatingMeta(lead,13));
        var title=TextRaw(lead.DisplayTitle,26);title.FontWeight=FontWeights.ExtraBold;title.LineHeight=30;title.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;title.Margin=new(0,8,0,10);title.MaxHeight=90;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);
        var summary=TextRaw(lead.DisplayDescription??"",14);summary.LineHeight=21;summary.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;summary.MaxHeight=columns>=5?126:84;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.Margin=new(0,0,0,0);summary.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");text.Children.Add(summary);
        Grid.SetColumn(text,1);story.Children.Add(text);
        grid.Children.Add(StoryButton(lead,story,lead.DisplayTitle));
        if(rest.Length>0)
        {
            var list=new StackPanel{Margin=wide?new(0):new(0,18,0,0)};
            for(var index=0;index<rest.Length;index++)
            {
                var item=rest[index];
                var row=new Grid{Margin=new(0,0,0,12)};row.ColumnDefinitions.Add(new(){Width=new GridLength(34)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                var number=new TextBlock{Text=(index+2).ToString(),FontFamily=(FontFamily)FindResource("DisplayFont"),FontSize=20,FontWeight=FontWeights.ExtraBold,VerticalAlignment=VerticalAlignment.Center};number.SetResourceReference(TextBlock.ForegroundProperty,"EdgeFocus");row.Children.Add(number);
                var thumb=StoryPoster(item,46,7);Grid.SetColumn(thumb,1);row.Children.Add(thumb);
                var info=new StackPanel{Margin=new(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};
                var name=TextRaw(item.DisplayTitle,14.5);name.FontWeight=FontWeights.SemiBold;name.Margin=new(0,0,0,3);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;info.Children.Add(name);info.Children.Add(RatingMeta(item));
                Grid.SetColumn(info,2);row.Children.Add(info);
                var frame=new Border{BorderThickness=new(0,0,0,1),Padding=new(0,0,0,12),Child=StoryButton(item,row,item.DisplayTitle)};frame.SetResourceReference(Border.BorderBrushProperty,index==rest.Length-1?"Bg":"EdgeSoft");
                list.Children.Add(frame);
            }
            if(wide)Grid.SetColumn(list,2);else Grid.SetRow(list,1);
            if(!wide){grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new());}
            grid.Children.Add(list);
        }
        host.Children.Add(grid);
    }

    // ── Numbered ranking ──
    // Large heavy numerals that overlap the lower left corner of each poster.
    void FillFeaturedRanking(StackPanel host,MediaItem[] items,int columns)
    {
        var key=FeaturedKey(items)+"|"+columns;
        if(host.Tag as string==key&&host.Children.Count>0)return;host.Tag=key;
        host.Children.Clear();
        var count=Math.Min(items.Length,Math.Clamp(columns,2,5));
        if(count==0)return;
        var grid=new UniformGrid{Columns=count,Margin=new(0,4,-16,0)};
        for(var index=0;index<count;index++)
        {
            var item=items[index];
            var cell=new StackPanel{Margin=new(0,0,16,18)};
            var stage=new Grid();
            var poster=StoryPoster(item);poster.Margin=new(34,0,0,0);stage.Children.Add(poster);
            // The numeral is drawn after the poster, so it lies over the poster's corner.
            var number=new TextBlock{Text=(index+1).ToString(),FontFamily=(FontFamily)FindResource("DisplayFont"),FontSize=92,FontWeight=FontWeights.ExtraBold,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(-2,0,0,34),IsHitTestVisible=false};
            number.SetResourceReference(TextBlock.ForegroundProperty,"Text");stage.Children.Add(number);
            cell.Children.Add(StoryButton(item,stage,item.DisplayTitle));
            var title=TextRaw(item.DisplayTitle,14);title.FontWeight=FontWeights.SemiBold;title.Margin=new(36,10,2,2);title.MaxHeight=36;title.TextTrimming=TextTrimming.CharacterEllipsis;cell.Children.Add(title);
            var meta=RatingMeta(item);if(meta is FrameworkElement line)line.Margin=new(36,0,2,0);cell.Children.Add(meta);
            grid.Children.Add(cell);
        }
        host.Children.Add(grid);
    }
}
