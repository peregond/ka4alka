using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;
public partial class MainWindow
{
    public async Task CatalogPagingSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();searchDelay.Stop();
        section="Фильмы";Search.Text="";searchDelay.Stop();current=null;favoritesOnly=false;ResetCatalogFilters();livePage=1;liveKey="";
        var rows=Enumerable.Range(1,120).Select(id=>new MediaItem(id,"Фильм "+id,"Фильмы",id%2==0?"комедия":"драма",2024,"8.2","8.0","#526B69")
            {GenreKeys=[id%2==0?"komediia":"drama"],CountryKeys=[id%3==0?"rossiia":"ssha"],Country=id%3==0?"Россия":"США"}).ToArray();
        foreach(var selection in new[]{new CatalogSelection(),new CatalogSelection(Genre:"komediia"),new CatalogSelection(Genre:"komediia",Country:"ssha"),new CatalogSelection(Genre:"komediia",Country:"ssha",Rating:8),new CatalogSelection(Collection:"popular"),new CatalogSelection(Collection:"rated")})
        {
            var filtered=rows.Where(selection.Matches).ToArray();
            for(var page=1;page<=Math.Max(1,(filtered.Length+39)/40);page++)
                catalogPages["Фильмы|"+selection.Filter+"|"+page]=new(filtered.Skip((page-1)*40).Take(40).ToArray(),page*40<filtered.Length,CatalogChoices.Genres,CatalogChoices.Countries);
        }
        foreach(var row in rows)cardMetadata[row.Id]=Task.FromResult(row);
        async Task Settle(){var end=DateTime.UtcNow.AddSeconds(15);while(liveLoading&&DateTime.UtcNow<end)await Task.Delay(30);await Task.Delay(100);UpdateLayout();if(liveLoading)throw new Exception("Catalog fixture timed out");}
        void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS: "+label+Environment.NewLine);}
        Button Page(int page)=>FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Страница "+page)??throw new Exception("Missing page "+page);
        void Choose(string name,string key)
        {
            var combo=FindVisual<ComboBox>(FiltersPanel,x=>AutomationProperties.GetName(x)==name)??FindVisual<ComboBox>(PageHeader,x=>AutomationProperties.GetName(x)==name)??throw new Exception("Missing filter "+name);
            combo.SelectedItem=((IEnumerable<CatalogChoice>)combo.ItemsSource).Single(x=>x.Key==key);
        }
        // Exercise desktop layouts even on the hosted runner's smaller virtual monitor.
        MaxWidth=1800;MaxHeight=1000;MinWidth=360;MinHeight=300;
        Width=1280;Height=800;Render();await Settle();
        Check(liveItems.Count==40&&catalogDisplay.Count==40,"one catalog page displays 40 cards");
        Check(FiltersPanel.Visibility==Visibility.Visible,"filters appear on the right at standard window width");
        Check(!VisualElements<Button>(Body).Any(b=>b.Content?.ToString()?.Contains("Показать ещё")==true),"catalog has no load-more button");
        var scroll=FindVisual<ScrollViewer>(Body,_=>true)!;
        var first=liveItems.Select(x=>x.Id).ToArray();
        var poster=VisualElements<Button>(catalogList!).First(x=>x.Tag is MediaItem);
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent};poster.RaiseEvent(wheel);await Task.Delay(250);UpdateLayout();
        Check(wheel.Handled&&scroll.VerticalOffset>0,"wheel scrolls cards within paged catalog");
        scroll.ScrollToEnd();await Task.Delay(100);UpdateLayout();
        Check(Page(2).TransformToAncestor(scroll).Transform(new Point()).Y>=0&&Page(2).TransformToAncestor(scroll).Transform(new Point()).Y<scroll.ViewportHeight,"page navigation is reachable below the cards");
        Page(2).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
        Check(livePage==2&&liveItems.Count==40&&!liveItems.Select(x=>x.Id).Intersect(first).Any(),"page 2 replaces page 1 without overlapping cards");
        Check(FindVisual<ScrollViewer>(Body,_=>true)!.VerticalOffset<1,"page change returns to top");
        Page(1).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();Check(liveItems.Select(x=>x.Id).SequenceEqual(first),"returning to page 1 restores its cards");
        Choose("Жанр","komediia");await Settle();Check(livePage==1&&liveItems.All(x=>x.GenreKeys.Contains("komediia")),"genre filter resets pagination and applies to source selection");
        Choose("Страна","ssha");await Settle();Check(liveItems.Count==40&&liveItems.All(x=>x.CountryKeys.Contains("ssha")),"country and genre filters combine");
        Choose("Рейтинг от","8");await Settle();Check(liveItems.Count==40&&catalogRating==8,"rating threshold combines with genre and country");
        ResetCatalogFilters();liveKey="";Render();await Settle();Choose("Подборка","popular");await Settle();Check(catalogCollection=="popular"&&CatalogSelection.Filter=="","popular collection uses public source order");
        Choose("Подборка","rated");await Settle();Check(CatalogSelection.Filter=="rating-8/sort-rating","high-rating collection uses source rating threshold");
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"catalog.png")))png.Save(file);
        Width=680;Height=500;await Task.Delay(150);UpdateLayout();Check(FiltersPanel.Visibility==Visibility.Collapsed&&inlineFilterButton?.Visibility==Visibility.Visible,"narrow window exposes inline filters");
        inlineFilterButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));UpdateLayout();Check(inlineCatalogFilters?.Visibility==Visibility.Visible,"inline filters open on narrow screens");
        Close();
    }
}
