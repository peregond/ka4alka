using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Kachalka;
public partial class MainWindow
{
    public async Task FranchiseSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();catalogRefreshTimer.Stop();searchDelay.Stop();
        var item=ImdbSuggestions.Card("tt0241527","Гарри Поттер и философский камень","Harry Potter and the Philosopher's Stone","Фильмы",2001,null) with{Description="Проверка встроенного раздела франшизы."};
        requestedDetails.Add(item.Id);current=item;activePerson=null;section="Фильмы";Width=1280;Height=820;Render();
        await Task.Delay(100);UpdateLayout();
        if(detailFranchiseHost?.Visibility!=Visibility.Collapsed||detailFranchiseTask!=null)throw new Exception("Franchises must not load before a click.");
        var action=FindVisual<Button>(Body,x=>System.Windows.Automation.AutomationProperties.GetName(x)==AppLanguage.T("Другие части"))??throw new Exception("Missing franchise action.");
        action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100);UpdateLayout();
        var grid=FindVisual<ItemsControl>(Body,x=>x.Name=="FranchiseFilms")??throw new Exception("Missing inline franchise grid.");
        var count=grid.Items.OfType<CatalogRow>().SelectMany(x=>x.Items).Count();
        if(count!=7||current.Id!=item.Id||detailFranchiseHost?.Visibility!=Visibility.Visible)throw new Exception("Other parts did not open in the same film page.");
        action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(detailFranchiseHost?.Visibility!=Visibility.Collapsed||current.Id!=item.Id)throw new Exception("Franchise collapse changed navigation.");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{ClickToLoad=true,SamePage=true,OtherParts=7,CurrentExcluded=true,Collapse=true}));Close();
    }
}
