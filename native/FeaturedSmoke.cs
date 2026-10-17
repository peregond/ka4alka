using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;
public partial class MainWindow
{
    public async Task FeaturedSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();catalogRefreshTimer.Stop();refresh.Stop();searchDelay.Stop();
        var originalLanguage=prefs.UiLanguage;var originalPoor=prefs.HidePoorQuality;var originalQuality=prefs.CatalogQualityHeight;
        var originalHiddenTitles=prefs.HiddenTitles;var originalHiddenGenres=prefs.HiddenGenres;
        var checks=new List<string>();
        void Check(bool pass,string message){if(!pass)throw new Exception(message);checks.Add(message);}
        async Task Settle(){await Task.Delay(100);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        prefs.CatalogQualityHeight=0;prefs.HidePoorQuality=false;prefs.HiddenTitles=[];prefs.HiddenGenres=[];
        var definition=FeaturedCatalog.Collections.First(row=>!row.IsGames);
        var items=Enumerable.Range(1,20).Select(i=>new MediaItem(-970000-i,"История "+i,i%2==0?"Фильмы":"Сериалы","драма",2024,"7.0","7.0","#526B69")
        {OnlineId="movies:featured-fixture-"+i,PageUrl=LiveCatalog.Base+"/movies/featured-fixture-"+i,Description="Описание",GenreKeys=["drama"]}).ToArray();
        foreach(var item in items)cardMetadata[item.Id]=Task.FromResult(item);
        var gameDefinition=FeaturedCatalog.Collections.Single(row=>row.IsGames);
        var game=GameCatalog.Item(620,"Portal 2");gameDetailProvider=(item,ct)=>Task.FromResult(new GameDetails(item,"","Valve","Valve"));gameSourcesProvider=_=>[];
        // The lead story with its list, a numbered ranking and an ordinary shelf, as the front page has them.
        var shelfDefinition=FeaturedCatalog.Collections.Where(row=>!row.IsGames&&row.Id!=definition.Id).First();
        var rankingDefinition=new FeaturedCollection(FeaturedCatalog.TopRatedId,"Лучшее по оценкам","Top rated","Фильмы и сериалы с самыми высокими оценками зрителей","Titles with the highest audience ratings",Layout:"ranking");
        FeaturedGroup[] fixtureGroups=[new(definition,items),new(rankingDefinition,items.Skip(5).Take(8).Concat([GameCatalog.Item(620,"Portal 2",FeaturedCatalog.GamePortrait(620)),GameCatalog.Item(413150,"Stardew Valley",FeaturedCatalog.GamePortrait(413150))]).ToArray()),new(shelfDefinition,items.Skip(8).Take(10).ToArray()),new(gameDefinition,[game])];
        var delayed=new TaskCompletionSource<FeaturedGroup[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        featuredProvider=_=>delayed.Task;featuredGroups=null;
        ShowFeatured();await Settle();
        Check(featuredState?.Visibility==Visibility.Visible&&featuredGroups==null,"A delayed snapshot shows the page scaffold without blocking input.");
        ShowDownloads(DownloadsButton,new RoutedEventArgs());var awayRender=renderCount;delayed.SetResult(fixtureGroups);await Settle();
        Check(section=="Загрузки"&&renderCount==awayRender&&featuredGroups==null,"A cancelled late result cannot replace another page.");
        delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);featuredProvider=_=>delayed.Task;ShowFeatured();await Settle();
        var loadingRender=renderCount;var loadingScroll=FindVisual<ScrollViewer>(Body,_=>true);delayed.SetResult(fixtureGroups);await Settle();
        Check(renderCount==loadingRender&&ReferenceEquals(loadingScroll,FindVisual<ScrollViewer>(Body,_=>true))&&featuredGroups!=null,"Loaded collections fill the existing scaffold in place.");
        featuredProvider=null;metadataLifetime.Cancel();
        MaxWidth=1800;MaxHeight=1100;MinWidth=700;MinHeight=550;Width=1280;Height=850;
        ShowFeatured();await Settle();
        Check(Navigation.Children.OfType<Button>().First().Tag?.ToString()==FeaturedSection,"Featured is the first library section.");
        Check(Navigation.Children.OfType<Button>().First().Content is Grid navGrid&&navGrid.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault()?.Fill!=null,"The icon of the open section is solid.");
        Check(Navigation.Children.OfType<Button>().Skip(1).All(button=>button.Content is Grid other&&other.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault()?.Fill==null),"The icons of the other sections stay outlined.");
        Check(FindVisual<ScrollViewer>(Body,_=>true) is {} pageScroll&&FindVisual<TextBlock>(pageScroll,text=>text.Name=="FeaturedHeading")!=null,"The page title and date scroll away with the stories.");
        Check(featuredHero!=null&&VisualElements<TextBlock>(featuredHero).Any(text=>text.Text=="КП")&&VisualElements<TextBlock>(featuredHero).Any(text=>text.Text=="IMDb")&&!VisualElements<TextBlock>(featuredHero).Any(text=>text.Text is "Фильм" or "Сериал"),"The banner shows both ratings with the mark of their site and no kind word.");
        Check(FindVisual<StackPanel>(Body,panel=>panel.Name=="FeaturedotherworldsBlock") is {} leadBlock&&VisualElements<TextBlock>(leadBlock).Any(text=>text.Text=="1")&&VisualElements<TextBlock>(leadBlock).Any(text=>text.Text=="2"),"The lead story is numbered one like the list beside it.");
        Check(SearchBar.Visibility==Visibility.Visible&&featuredShelves.Count==4,"Search and editorial shelves are available.");
        Check(featuredShelves[rankingDefinition.Id].Custom!=null&&FindVisual<StackPanel>(Body,panel=>panel.Name=="FeaturedtopratedBlock")?.Children.OfType<UniformGrid>().FirstOrDefault()?.Children.Count>=2,"The ranking shows numbered posters.");
        // Films, series and games each have a tab in the ranking; a tab shows its own list.
        var rankingBlock=FindVisual<StackPanel>(Body,panel=>panel.Name=="FeaturedtopratedBlock")!;
        var rankingTabs=VisualElements<Button>(rankingBlock).Where(button=>button.Name.StartsWith("FeaturedRankingTab",StringComparison.Ordinal)).ToArray();
        Check(rankingTabs.Length==3,"The ranking has a tab for each kind it holds.");
        rankingTabs[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        rankingBlock=FindVisual<StackPanel>(Body,panel=>panel.Name=="FeaturedtopratedBlock")!;
        Check(VisualElements<Button>(rankingBlock).Count(button=>button.Tag is MediaItem {Section:"Игры"})==2,"The games tab of the ranking shows the games.");
        VisualElements<Button>(rankingBlock).First(button=>button.Name=="FeaturedRankingTab0").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        var scroll=FindVisual<ScrollViewer>(Body,_=>true);var baseline=renderCount;
        RefreshFeaturedRows();await Settle();
        Check(renderCount==baseline&&ReferenceEquals(scroll,FindVisual<ScrollViewer>(Body,_=>true)),"Shelf updates preserve the page and scroll viewer.");
        var first=items[0];prefs.HiddenTitles.Add(HiddenCatalog.Title(first));RefreshFeaturedRows();
        Check(featuredShelves[definition.Id].Items.All(item=>item.Id!=first.Id),"Hidden titles disappear from the curated shelf in place.");prefs.HiddenTitles.Clear();RefreshFeaturedRows();
        OpenFeaturedCollection(definition.Id);await Settle();
        Check(catalogDisplay.Count==20&&catalogRows?.Count>0,"More opens the entire collection in the virtualized list.");
        var card=VisualElements<Button>(Body).First(button=>button.Tag is MediaItem);Click(card);await Settle();
        Check(current!=null&&FeaturedBackLabel()==FeaturedSection,"A curated title opens its normal detail with a return path.");
        CinemaBack();await Settle();Check(section==FeaturedSection&&featuredCollectionId==definition.Id&&current==null,"Back returns to the same collection.");
        ShowFeatured();OpenFeaturedCollection(gameDefinition.Id);await Settle();Check(GameGrid,"A curated game collection uses wide game cards.");
        card=VisualElements<Button>(Body).First(button=>button.Tag is MediaItem);Click(card);await Settle();GameBack();await Settle();
        Check(section==FeaturedSection&&featuredCollectionId==gameDefinition.Id,"Back from a game also returns to the curated collection.");
        // An award page lists every year: the winner first, the nominees after it, each with its own tag.
        var awardDefinition=new FeaturedCollection("award-oscar-best-picture","Оскар","Academy Awards","Лучший фильм","Best Picture");
        AwardEntry Entry(int year,string imdb,string title)=>new(year,imdb,title,title,title,year-1,null);
        Awards.Seed([new("oscar-best-picture","Оскар","Academy Awards","Лучший фильм","Best Picture",[Entry(2025,"tt0000101","Победитель 2025"),Entry(2024,"tt0000102","Победитель 2024")])],
            new(){["oscar-best-picture"]=[Entry(2025,"tt0000103","Номинант А"),Entry(2025,"tt0000104","Номинант Б"),Entry(2024,"tt0000105","Номинант В")]});
        featuredGroups=[..fixtureGroups,new(awardDefinition,[])];
        ShowFeatured();OpenFeaturedCollection(awardDefinition.Id);await Settle();
        Check(VisualElements<TextBlock>(Body).Count(text=>text.Text is "2025" or "2024")==2,"The award page has a heading for each year.");
        Check(VisualElements<Button>(Body).Count(button=>button.Tag is MediaItem)==5,"Each year lists its winner and its nominees.");
        Check(VisualElements<TextBlock>(Body).Count(text=>text.Text=="Победитель")==2&&VisualElements<TextBlock>(Body).Count(text=>text.Text=="Номинант")==3,"Winners and nominees carry their own tag.");
        Point Where(TextBlock text)=>text.TransformToAncestor(Body).Transform(new Point());
        var firstWinner=Where(VisualElements<TextBlock>(Body).First(text=>text.Text=="Победитель"));var firstNominee=Where(VisualElements<TextBlock>(Body).First(text=>text.Text=="Номинант"));
        Check(firstWinner.Y<firstNominee.Y||firstWinner.Y==firstNominee.Y&&firstWinner.X<firstNominee.X,"The winner of the newest year comes before its nominees.");
        featuredGroups=fixtureGroups;ShowFeatured();await Settle();
        foreach(var language in new[]{"ru","en"})
        {
            prefs.UiLanguage=language;AppLanguage.Set(language);ShowFeatured();await Settle();
            // A new interface language resolves the shelves again; wait for that load like a person would.
            metadataLifetime.Dispose();metadataLifetime=new();featuredProvider=_=>Task.FromResult(fixtureGroups);
            for(var wait=0;wait<200&&featuredGroups==null;wait++)await Task.Delay(50);await Settle();
            Check(featuredGroups!=null&&featuredLanguage==language,"Shelves are resolved again after the interface language changes: "+language);
            featuredProvider=null;
            Check((FindVisual<TextBlock>(Body,text=>text.Name=="FeaturedHeading")??FindVisual<TextBlock>(PageHeader,text=>text.Name=="FeaturedHeading"))?.Text==AppLanguage.T(FeaturedSection),"Featured title translates: "+language);
            foreach(var width in new[]{760d,1280d})
            {
                Width=width;await Settle();ResizeFeaturedShelves();
                Check(featuredShelves.Values.All(shelf=>shelf.Custom!=null||shelf.Row.Content is CatalogRow row&&row.Columns>=1),"Responsive shelves: "+language+" "+width);
                Check(carousel!=null&&FindVisual<Grid>(Body,grid=>grid.Name=="FeaturedHero")?.Children.Count>0,"The banner of top stories is shown: "+language+" "+width);
                Check(FindVisual<StackPanel>(Body,panel=>panel.Name=="FeaturedDigest")?.Children.OfType<UniformGrid>().Any(row=>row.Children.Count>=1)==true,"The digest of stories is shown: "+language+" "+width);
                Check(FindVisual<StackPanel>(Body,panel=>panel.Name.EndsWith("Block",StringComparison.Ordinal)&&panel.Name.StartsWith("Featured",StringComparison.Ordinal))?.Children.Count>0,"The lead story and its list are drawn: "+language+" "+width);
                void Capture(string suffix)
                {
                    var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"featured-"+language+"-"+width+suffix+".png"));png.Save(file);
                }
                Capture("");
                // The same page scrolled down, so the lower rubrics can be inspected too.
                if(width>1000&&FindVisual<ScrollViewer>(Body,_=>true) is {} page)
                {
                    foreach(var offset in new[]{650d,1300d,1950d}){page.ScrollToVerticalOffset(offset);await Settle();Capture("-y"+(int)offset);}
                    page.ScrollToVerticalOffset(0);await Settle();
                }
            }
        }
        prefs.UiLanguage=originalLanguage;AppLanguage.Set(originalLanguage);prefs.HidePoorQuality=originalPoor;prefs.CatalogQualityHeight=originalQuality;
        prefs.HiddenTitles=originalHiddenTitles;prefs.HiddenGenres=originalHiddenGenres;
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(checks));Close();
    }
}
