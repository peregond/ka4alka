using System.Windows;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    // Resolve aliases when the actions become visible, not on every image arrival.
    void CatalogPosterEntered(object sender,RoutedEventArgs args)
    {
        if(sender is Button {Tag:MediaItem item})item.SetSavedForUi(IsSaved(item));
    }
    void CatalogPosterContextChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if(sender is Button button&&(button.IsMouseOver||button.IsKeyboardFocusWithin)&&args.NewValue is MediaItem item)item.SetSavedForUi(IsSaved(item));
    }
    void CatalogPosterSave(object sender,RoutedEventArgs args)
    {
        args.Handled=true; // Never invoke the enclosing poster's OpenCard handler.
        if(sender is not Button {DataContext:MediaItem item})return;
        var ids=new HashSet<int>(prefs.Favorites);var saved=prefs.LiveFavorites.ToList();
        try
        {
            ToggleSaved(item);var selected=IsSaved(item);item.SetSavedForUi(selected);
            foreach(var card in VisualElements<Button>(Body).Select(x=>x.Tag).OfType<MediaItem>().DistinctBy(x=>x.Id))
                if(card.Id==item.Id||MediaIdentity.Same(card,item))card.SetSavedForUi(selected);
            Status.Text=AppLanguage.T(selected?"Добавлено в сохранённое.":"Удалено из сохранённого.");
            if(section=="Сохранённое"||favoritesOnly)Render();
        }
        catch(Exception error){prefs.Favorites=ids;prefs.LiveFavorites=saved;Status.Text=AppLanguage.T("Не удалось сохранить выбор: "+error.Message);}
    }
    void CatalogPosterHide(object sender,RoutedEventArgs args)
    {
        args.Handled=true;
        if(sender is not Button {DataContext:MediaItem item})return;
        SaveCatalogPersonalChoice(()=>
        {
            if(HiddenCatalog.TitleHidden(item,prefs.HiddenTitles))return;
            if(prefs.HiddenTitles.Count>=1000)throw new InvalidOperationException("В списке уже 1000 названий. Верни несколько из них в настройках.");
            prefs.HiddenTitles.Add(HiddenCatalog.Title(item));
        },true);
    }
}
