using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kachalka;
public partial class MainWindow
{
    void InitializeShareLinks()
    {
        // A link pasted into the search field opens its card instead of becoming a search query.
        DataObject.AddPastingHandler(Search,(_,e)=>
        {
            if(!e.DataObject.GetDataPresent(DataFormats.UnicodeText)||e.DataObject.GetData(DataFormats.UnicodeText) is not string text||ShareLink.Parse(text)==null)return;
            e.CancelCommand();Dispatcher.BeginInvoke(new Action(()=>OpenShareLink(text)));
        });
        // Ctrl+V anywhere outside a text field does the same, as it adds a magnet link on the downloads page.
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key!=Key.V||!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)||Keyboard.FocusedElement is TextBox)return;
            string text;try{text=Clipboard.ContainsText()?Clipboard.GetText():"";}catch{return;}
            if(ShareLink.Parse(text)==null)return;
            e.Handled=true;OpenShareLink(text);
        };
    }
    // Opens the card a kachalka:// link points to. False when the text holds no such link.
    internal bool OpenShareLink(string? text)
    {
        if(ShareLink.Parse(text) is not {} target)return false;
        if(closing||closed||!ready)return true;
        var card=DownloadMetadata.Card(new(){MediaTitle=target.Title.Length>0?target.Title:target.Slug,MediaSection=target.Section,MediaPageUrl=target.PageUrl,MediaYear=target.Year});
        if(card==null)return false;
        searchDelay.Stop();
        activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;downloadReturnItem=null;
        current=DownloadMetadata.EnrichMedia(card,new[]{sharedCatalog.Find(card)}.OfType<MediaItem>().Concat(prefs.LiveFavorites).Concat(liveItems).Concat(catalogIndex.Recent(card.Section,200)).Concat(BundledCatalog.Search(card.Section,card.Title)));
        section=current.Section;
        if(Search.Text.Length>0&&ShareLink.Parse(Search.Text)!=null)Search.Text="";
        Render();
        DiagnosticLog.Write("share-link-opened",new{target.Section,target.Slug});
        return true;
    }
    // A link sent by a second copy of Качалка that a click on a kachalka:// link started.
    internal void ReceiveShareLink(string text)
    {
        if(closing||closed)return;
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Show();Activate();
        OpenShareLink(text);
    }
    void CopyShareLink(MediaItem item)
    {
        if(ShareLink.Create(item) is not {} link)return;
        try{Clipboard.SetText(link);Status.Text=AppLanguage.T("Ссылка на «"+item.Title+"» скопирована. Её можно открыть в Качалке или вставить в поиск.");}
        catch(System.Runtime.InteropServices.COMException){Status.Text=AppLanguage.T("Не удалось скопировать ссылку: буфер обмена занят другим приложением.");}
    }
}
