using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    void AssertPersonPageLayout(bool narrow)
    {
        UpdateLayout();
        var hero=FindVisual<Border>(Body,element=>element.Name=="PersonHero")??throw new Exception("Person hero is missing.");
        var biography=FindVisual<TextBlock>(hero,element=>element.Name=="PersonBiography")??throw new Exception("Biography must be inside the person hero.");
        var name=FindVisual<TextBlock>(hero,element=>element.Name=="PersonName")??throw new Exception("Person name is missing.");
        var panel=FindVisual<StackPanel>(hero,element=>element.Name=="PersonBiographyPanel")!;
        var grid=FindVisual<Grid>(hero,element=>element.Name=="PersonHeroContent")!;
        if(biography.Text is "Загружаем биографию…" or "Биография пока недоступна.")throw new Exception("Cached person biography did not reach the hero.");
        var status=FindVisual<TextBlock>(Body,element=>element.Name=="PersonFilmographyStatus")??throw new Exception("Person filmography status is missing.");
        if(status.Text.Contains("загружаем",StringComparison.OrdinalIgnoreCase))throw new Exception("Settled cached filmography reverted to a loading state.");
        if(!narrow&&panel.Parent is not StackPanel{Name:"PersonIdentity"})throw new Exception("Wide biography should sit beside the portrait and under the name.");
        if(narrow&&grid.ActualWidth<600&&panel.Parent!=grid)throw new Exception("Narrow biography should use the full hero width.");
        var nameTop=name.TransformToAncestor(hero).Transform(new Point()).Y;
        var biographyTop=biography.TransformToAncestor(hero).Transform(new Point()).Y;
        if(biographyTop<nameTop+name.ActualHeight-1)throw new Exception("Biography overlaps the person's name.");
        foreach(var element in new FrameworkElement[]{hero,name,biography})
        {
            var bounds=element.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),element.RenderSize));
            if(bounds.Left<-.5||bounds.Right>Body.ActualWidth+1)throw new Exception("Person page overflows horizontally: "+element.Name);
        }
        if(FindVisual<Border>(Body,element=>element.Name=="PersonAwardFilmsCard") is {IsVisible:true})throw new Exception("A person with no confirmed awards shows an empty awards section.");
    }

    void AssertPersonFilmGrid(int minimumCount=2)
    {
        UpdateLayout();
        var gallery=FindVisual<ItemsControl>(Body,element=>element.Name=="PersonFilmographyGrid")??throw new Exception("Filmography poster gallery is missing.");
        var rows=gallery.Items.OfType<CatalogRow>().ToArray();
        if(rows.Sum(row=>row.Items.Length)<minimumCount)throw new Exception("Confirmed person works were not shown in the gallery.");
        foreach(var button in VisualElements<Button>(gallery).Where(button=>button.Tag is MediaItem))
        {
            var film=(MediaItem)button.Tag;
            if(AutomationProperties.GetName(button)!="Открыть "+film.Title)throw new Exception("Filmography tile lost its named navigation action.");
            var poster=FindVisual<Border>(button,element=>element.Name=="CardPoster")??throw new Exception("Filmography must use catalog poster cards.");
            if(poster.ActualWidth<1||Math.Abs(poster.ActualHeight-poster.ActualWidth*1.5)>1.5)throw new Exception("Filmography poster does not keep the catalog aspect ratio.");
            if(poster.Clip is not RectangleGeometry{RadiusX:>0,RadiusY:>0})throw new Exception("Filmography poster artwork is not clipped to rounded corners.");
            foreach(var title in VisualElements<TextBlock>(button).Where(text=>text.Text==film.Title))
            {
                var bounds=title.TransformToAncestor(button).TransformBounds(new Rect(new Point(),title.RenderSize));
                if(bounds.Left<-.5||bounds.Right>button.ActualWidth+1)throw new Exception("Filmography title crosses its card edge.");
            }
        }
        if(FindVisual<ItemsControl>(Body,element=>element.Name=="PersonTopFilms") is {IsVisible:true} top)
        {
            var movies=top.Items.OfType<CatalogRow>().SelectMany(row=>row.Items).ToArray();
            if(movies.Any(movie=>CinemaMetadata.Rating(movie)<=0)||!movies.Select(CinemaMetadata.Rating).SequenceEqual(movies.Select(CinemaMetadata.Rating).OrderByDescending(rating=>rating)))throw new Exception("Person ranking uses unavailable scores or ignores real ratings.");
        }
    }
}
