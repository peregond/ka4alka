using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    IEnumerable<CatalogChoice> PersonalGenreChoices=>CatalogChoices.Genres.Concat(catalogGenres).DistinctBy(choice=>choice.Key);
    bool PersonallyVisible(MediaItem item)=>!HiddenCatalog.Hidden(item,prefs,PersonalGenreChoices);
    ContextMenu CatalogPersonalMenu(MediaItem item)
    {
        var menu=new ContextMenu();menu.SetResourceReference(Control.BackgroundProperty,"Panel");menu.SetResourceReference(Control.ForegroundProperty,"Text");
        void Save(Action change)
        {
            var titles=prefs.HiddenTitles.ToList();var genres=new HashSet<string>(prefs.HiddenGenres);
            try{change();prefs.Save();if(section=="Игры")Dispatcher.BeginInvoke(new Action(()=>{if(section=="Игры"&&current==null)Render();}));else ScheduleCatalogEvidenceRefresh();Status.Text="Каталог настроен. Вернуть скрытое можно в настройках.";}
            catch(Exception error){prefs.HiddenTitles=titles;prefs.HiddenGenres=genres;Status.Text="Не удалось сохранить выбор: "+error.Message;}
        }
        var hidden=HiddenCatalog.TitleHidden(item,prefs.HiddenTitles);
        var title=new MenuItem{Header=hidden?"Вернуть в каталог":item.Section=="Сериалы"?"Скрыть сериал из каталога":item.Section=="Игры"?"Скрыть игру из каталога":"Скрыть фильм из каталога"};
        title.Click+=(_,_)=>Save(()=>
        {
            if(hidden)prefs.HiddenTitles.RemoveAll(row=>row.Section==item.Section&&(row.CatalogId==item.Id||row.Identity==HiddenCatalog.Title(item).Identity));
            else{if(prefs.HiddenTitles.Count>=1000)throw new InvalidOperationException("В списке уже 1000 названий. Верни несколько из них в настройках.");prefs.HiddenTitles.Add(HiddenCatalog.Title(item));}
        });menu.Items.Add(title);
        var genreChoices=HiddenCatalog.Genres(item,PersonalGenreChoices);
        if(genreChoices.Length>0)
        {
            var group=new MenuItem{Header="Скрыть жанр"};
            foreach(var choice in genreChoices)
            {
                var option=new MenuItem{Header=choice.Label,IsCheckable=true,IsChecked=prefs.HiddenGenres.Contains(choice.Key)};
                option.Click+=(_,_)=>Save(()=>{if(option.IsChecked)prefs.HiddenGenres.Add(choice.Key);else prefs.HiddenGenres.Remove(choice.Key);});group.Items.Add(option);
            }
            menu.Items.Add(group);
        }
        return menu;
    }
    void CatalogPosterMenu(object sender,ContextMenuEventArgs args)
    {
        if(sender is Button button&&button.Tag is MediaItem item)button.ContextMenu=CatalogPersonalMenu(item);
    }
    void RenderHiddenCatalogSettings(StackPanel target)
    {
        var genres=new WrapPanel();target.Children.Add(genres);
        var search=new TextBox{ToolTip="Найти скрытое название",Margin=new(0,12,0,0)};target.Children.Add(Text("Поиск по скрытым названиям",12,true));target.Children.Add(search);
        var titleRows=new StackPanel{Margin=new(0,16,0,0)};target.Children.Add(titleRows);
        var note=Text("",12,true);target.Children.Add(note);
        void UpdateTitles()
        {
            titleRows.Children.Clear();
            titleRows.Children.Add(Text("Скрытые названия: "+prefs.HiddenTitles.Count,14));
            var rows=new StackPanel();
            foreach(var hidden in prefs.HiddenTitles.Where(row=>row.Title.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).Take(30))
            {
                var row=new DockPanel{Margin=new(0,4,0,4)};
                var restore=Button("Вернуть",()=>
                {
                    var previous=prefs.HiddenTitles.ToList();prefs.HiddenTitles.Remove(hidden);
                    try{prefs.Save();UpdateTitles();note.Text="Название возвращено в каталог.";}
                    catch(Exception error){prefs.HiddenTitles=previous;note.Text="Не удалось сохранить: "+error.Message;}
                });restore.Style=(Style)FindResource("QuietButton");DockPanel.SetDock(restore,Dock.Right);row.Children.Add(restore);
                row.Children.Add(Text(hidden.Title+(hidden.Year>0?" · "+hidden.Year:"")+" · "+hidden.Section,13));rows.Children.Add(row);
            }
            titleRows.Children.Add(new ScrollViewer{Content=rows,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        }
        foreach(var choice in PersonalGenreChoices.OrderBy(choice=>choice.Label))
        {
            var box=new CheckBox{Content=choice.Label,IsChecked=prefs.HiddenGenres.Contains(choice.Key),Margin=new(0,0,16,10),MinWidth=125};
            box.ToolTip="Не показывать жанр в каталоге и подборках";
            box.Click+=(_,_)=>
            {
                var previous=new HashSet<string>(prefs.HiddenGenres);
                if(box.IsChecked==true)prefs.HiddenGenres.Add(choice.Key);else prefs.HiddenGenres.Remove(choice.Key);
                try{prefs.Save();note.Text="Выбор жанров сохранён.";}
                catch(Exception error){prefs.HiddenGenres=previous;box.IsChecked=previous.Contains(choice.Key);note.Text="Не удалось сохранить: "+error.Message;}
            };genres.Children.Add(box);
        }
        search.TextChanged+=(_,_)=>UpdateTitles();
        target.Children.Add(Text("Показываем до 30 названий. Для остальных используй поиск выше.",11,true));
        UpdateTitles();
    }
}
