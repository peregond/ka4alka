using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Kachalka;

// The page of one award (Academy Awards, Cannes): every year with its winner first and its nominees after it.
// The winners are known as soon as the page opens; the nominees come from their own file and fill in when it is read.
public partial class MainWindow
{
    StackPanel? awardYears;
    string? awardYearsKey;
    bool awardNomineesAsked;
    bool IsAwardPage=>section==FeaturedSection&&current==null&&featuredCollectionId.StartsWith("award-",StringComparison.Ordinal);
    AwardCollection? OpenAward=>Awards.All.FirstOrDefault(row=>"award-"+row.Id==featuredCollectionId);

    void BuildAwardPage()
    {
        awardYearsKey=null;awardNomineesAsked=false;
        awardYears=new StackPanel{Name="AwardYears"};
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=awardYears,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    void CancelAwardPage(){awardYears=null;awardYearsKey=null;awardNomineesAsked=false;}

    async void LoadAwardNominees()
    {
        if(awardNomineesAsked||closed)return;awardNomineesAsked=true;
        try
        {
            await Awards.RefreshNomineesAsync(metadataLifetime.Token);
            if(!closed&&IsAwardPage)FillAwardYears();
        }
        catch(OperationCanceledException){}
        catch(Exception error){ErrorLog.Write(error);}
    }

    void FillAwardYears()
    {
        if(awardYears==null||!IsAwardPage)return;
        var award=OpenAward;
        if(award==null)
        {
            awardYears.Children.Clear();awardYearsKey=null;
            if(featuredState!=null){featuredState.Text=AppLanguage.T("В этой подборке пока нет доступных карточек.");featuredState.Visibility=Visibility.Visible;}
            return;
        }
        var nominees=Awards.NomineesOf(award.Id);
        var width=Body.ActualWidth>0?Body.ActualWidth:Math.Max(320,Width-280);
        var columns=WindowSizing.PosterColumns(width);
        var key=string.Join("|",award.Id,award.Entries.Length,nominees.Count,columns,AppLanguage.Current);
        if(key==awardYearsKey&&awardYears.Children.Count>0)return;awardYearsKey=key;
        awardYears.Children.Clear();
        if(featuredState!=null)featuredState.Visibility=Visibility.Collapsed;
        var byImdb=sharedCatalog.All.Where(item=>item.ImdbId!=null).GroupBy(item=>item.ImdbId!,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.OrdinalIgnoreCase);
        var shown=new List<MediaItem>();
        foreach(var year in award.Entries.Select(entry=>entry.Year).Concat(nominees.Select(entry=>entry.Year)).Distinct().OrderByDescending(year=>year))
        {
            var winners=award.Entries.Where(entry=>entry.Year==year).ToArray();
            var others=nominees.Where(entry=>entry.Year==year&&winners.All(winner=>winner.ImdbId!=entry.ImdbId)).ToArray();
            var cards=winners.Select(entry=>(Item:AwardsCatalog.Item(entry,byImdb),Winner:true)).Concat(others.Select(entry=>(Item:AwardsCatalog.Item(entry,byImdb),Winner:false))).Where(card=>PersonallyVisible(card.Item)).ToArray();
            if(cards.Length==0)continue;
            var heading=TextRaw(year.ToString(),26);heading.FontFamily=(FontFamily)FindResource("DisplayFont");heading.FontWeight=FontWeights.ExtraBold;heading.Margin=new(0,YearGap(awardYears),0,12);
            awardYears.Children.Add(heading);
            var grid=new UniformGrid{Columns=Math.Max(1,columns),Margin=new(0,0,-16,0)};
            foreach(var (item,winner) in cards)
            {
                shown.Add(item);
                var cell=new StackPanel{Margin=new(0,0,16,18)};
                cell.Children.Add(StoryButton(item,StoryPoster(item),item.DisplayTitle));
                var label=TextRaw(AppLanguage.T(winner?"Победитель":"Номинант"),11);label.FontWeight=winner?FontWeights.Bold:FontWeights.SemiBold;
                var tag=new Border{CornerRadius=new(6),Padding=new(8,2,8,3),Margin=new(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left,Child=label};
                tag.SetResourceReference(Border.BackgroundProperty,winner?"AccentSoft":"Panel");label.SetResourceReference(TextBlock.ForegroundProperty,winner?"Accent":"Subtle");
                cell.Children.Add(tag);
                var title=TextRaw(item.DisplayTitle,14);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,6,2,2);title.MaxHeight=36;title.TextTrimming=TextTrimming.CharacterEllipsis;cell.Children.Add(title);
                cell.Children.Add(RatingMeta(item));
                grid.Children.Add(cell);
            }
            awardYears.Children.Add(grid);
        }
        ApplyPosterDownloads(shown.ToArray());
    }
    static double YearGap(Panel panel)=>panel.Children.Count==0?0:12;
}
