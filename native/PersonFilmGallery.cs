using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    sealed class PersonGalleryView(Border frame,ObservableCollection<CatalogRow> rows)
    {
        public Border Frame=>frame;
        MediaItem[] films=[];
        int columns=1;
        public void Update(MediaItem[] items)
        {
            var unchanged=films.Length==items.Length&&films.Zip(items).All(pair=>SameFilm(pair.First,pair.Second));
            films=items;if(!unchanged)Reflow();
        }
        public void Resize(double width)
        {
            var next=WindowSizing.PosterColumns(width);if(next==columns)return;columns=next;Reflow();
        }
        static bool SameFilm(MediaItem a,MediaItem b)=>a.Id==b.Id&&a.Title==b.Title&&a.Year==b.Year&&a.Kinopoisk==b.Kinopoisk&&a.Imdb==b.Imdb&&a.ImageUrl==b.ImageUrl&&a.Genre==b.Genre;
        void Reflow()
        {
            var next=films.Chunk(columns).Select(items=>new CatalogRow(items,columns)).ToArray();
            for(var index=0;index<next.Length;index++)
            {
                if(index>=rows.Count){rows.Add(next[index]);continue;}
                var previous=rows[index];var replacement=next[index];
                if(previous.Columns!=replacement.Columns||previous.Items.Length!=replacement.Items.Length||!previous.Items.Zip(replacement.Items).All(pair=>SameFilm(pair.First,pair.Second)))rows[index]=replacement;
            }
            while(rows.Count>next.Length)rows.RemoveAt(rows.Count-1);
        }
    }
    PersonGalleryView PersonGallery(string title,string caption,string name)
    {
        var content=new StackPanel();var heading=Text(title,21);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,5);content.Children.Add(heading);
        var subtitle=Text(caption,11,true);subtitle.Margin=new(0,0,0,16);content.Children.Add(subtitle);
        var rows=new ObservableCollection<CatalogRow>();
        var grid=new ItemsControl{Name=name,ItemTemplate=(DataTemplate)FindResource("MediaRow"),ItemsSource=rows,Margin=new(0,0,-10,0)};content.Children.Add(grid);
        var frame=PersonPanel(content,name+"Card");frame.Margin=new(0,0,0,20);
        var view=new PersonGalleryView(frame,rows);grid.Tag=view;
        grid.SizeChanged+=(_,_)=>view.Resize(grid.ActualWidth);
        grid.Loaded+=(_,_)=>view.Resize(grid.ActualWidth);
        return view;
    }
}
