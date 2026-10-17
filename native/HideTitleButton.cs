using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace Kachalka;

// The eye on the open card of a film, series or game: the same "hide" the posters have, so a title can be put aside
// from where it is read. It is lit while the title is hidden and then brings it back.
public partial class MainWindow
{
    Button HideTitleButton(MediaItem item,string name)
    {
        var button=new Button{Name=name,Style=(Style)FindResource("IconButton"),Width=52,Height=52,Margin=new(0,0,10,8)};
        button.SetResourceReference(Control.BackgroundProperty,"BannerAction");button.SetResourceReference(Control.BorderBrushProperty,"BannerEdge");
        void Paint()
        {
            var hidden=HiddenCatalog.TitleHidden(item,prefs.HiddenTitles);
            button.Content=IconLabel("","IconEyeOff",20);
            if(hidden)button.SetResourceReference(Control.ForegroundProperty,"Accent");else button.Foreground=(System.Windows.Media.Brush)FindResource("BannerText");
            var label=hidden?"Вернуть в каталог":item.Section=="Сериалы"?"Скрыть сериал из каталога":item.Section=="Игры"?"Скрыть игру из каталога":"Скрыть фильм из каталога";
            button.ToolTip=AppLanguage.T(label);AutomationProperties.SetName(button,AppLanguage.T(label));
        }
        Paint();
        button.Click+=(_,_)=>
        {
            var hidden=HiddenCatalog.TitleHidden(item,prefs.HiddenTitles);
            SaveCatalogPersonalChoice(()=>
            {
                if(hidden)prefs.HiddenTitles.RemoveAll(row=>row.Section==item.Section&&(row.CatalogId==item.Id||row.Identity==HiddenCatalog.Title(item).Identity));
                else{if(prefs.HiddenTitles.Count>=1000)throw new InvalidOperationException("В списке уже 1000 названий. Верни несколько из них в настройках.");prefs.HiddenTitles.Add(HiddenCatalog.Title(item));}
            });
            Paint();
        };
        return button;
    }
}
