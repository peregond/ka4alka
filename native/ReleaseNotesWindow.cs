using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kachalka;

public enum ReleaseNotesKind { Subheading, Bullet, Paragraph }
public sealed record ReleaseNotesBlock(ReleaseNotesKind Kind,string Text);
public sealed record ReleaseNotesDocument(string Version,string Title,IReadOnlyList<ReleaseNotesBlock> Blocks,Uri Address);

// The notes of a published release of this application. Only this repository's own release is accepted,
// the answer is size-limited, and the Markdown text becomes a few block kinds.
public static class ReleaseNotesSource
{
    public static readonly Uri Latest=new Uri("https://api.github.com/repos/"+UpdateManifest.Repository+"/releases/latest");
    public static readonly Uri Page=new Uri("https://github.com/"+UpdateManifest.Repository+"/releases/latest");
    public static Uri ForVersion(string version)=>Regex.IsMatch(version,@"\A\d+\.\d+\.\d+\z")?new Uri("https://api.github.com/repos/"+UpdateManifest.Repository+"/releases/tags/v"+version):Latest;

    public static ReleaseNotesDocument Parse(byte[] json)
    {
        if(json.Length>256*1024)throw new InvalidDataException("Описание выпуска слишком большое.");
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Описание выпуска недействительно.");
        string? Field(string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
        bool Flag(string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;
        var tag=Field("tag_name");
        if(tag==null||!Regex.IsMatch(tag,@"\Av\d+\.\d+\.\d+\z")||Flag("draft")||Flag("prerelease")||!Uri.TryCreate(Field("html_url"),UriKind.Absolute,out var address)
            ||address.Scheme!="https"||address.Host!="github.com"||!address.AbsolutePath.StartsWith("/"+UpdateManifest.Repository+"/releases/",StringComparison.Ordinal))
            throw new InvalidDataException("Описание выпуска недействительно.");
        var body=(Field("body")??"").Replace("\r\n","\n").Trim();
        if(body.Length==0||body.Length>20_000)throw new InvalidDataException("Описание выпуска недействительно.");
        var title=Field("name")??tag;var blocks=new List<ReleaseNotesBlock>();var paragraph=new List<string>();var first=true;
        void Flush(){if(paragraph.Count>0){blocks.Add(new(ReleaseNotesKind.Paragraph,string.Join(" ",paragraph)));paragraph.Clear();}}
        foreach(var raw in body.Split('\n'))
        {
            var line=raw.Trim();
            if(line.Length==0){Flush();continue;}
            if(line.StartsWith("# ",StringComparison.Ordinal))
            {
                Flush();
                // The first heading is the headline of the release; the window shows it as its title.
                if(first)title=line[2..].Trim();else blocks.Add(new(ReleaseNotesKind.Subheading,line[2..].Trim()));
            }
            else if(line.StartsWith("## ",StringComparison.Ordinal)||line.StartsWith("### ",StringComparison.Ordinal)){Flush();blocks.Add(new(ReleaseNotesKind.Subheading,line.TrimStart('#').Trim()));}
            else if(line.StartsWith("- ",StringComparison.Ordinal)||line.StartsWith("* ",StringComparison.Ordinal)){Flush();blocks.Add(new(ReleaseNotesKind.Bullet,line[2..].Trim()));}
            else paragraph.Add(line);
            first=false;
        }
        Flush();
        if(blocks.Count==0)throw new InvalidDataException("Описание выпуска недействительно.");
        return new(tag[1..],title,blocks,address);
    }

    // Bold and code inside one line of the notes; a link keeps its text.
    public static IReadOnlyList<(string Text,bool Bold,bool Code)> Inline(string text)
    {
        text=Regex.Replace(text,@"\[([^\]]+)\]\(https?://[^)\s]+\)","$1");
        var runs=new List<(string Text,bool Bold,bool Code)>();
        foreach(var part in Regex.Split(text,@"(\*\*[^*]+\*\*|`[^`]+`)"))
        {
            if(part.Length==0)continue;
            if(part.Length>4&&part.StartsWith("**",StringComparison.Ordinal)&&part.EndsWith("**",StringComparison.Ordinal))runs.Add((part[2..^2],true,false));
            else if(part.Length>2&&part[0]=='`'&&part[^1]=='`')runs.Add((part[1..^1],false,true));
            else runs.Add((part,false,false));
        }
        return runs;
    }
}

public partial class MainWindow
{
    // "Что нового": the notes of the offered (or else the newest) release in a window of the application, not a jump to the GitHub page.
    void ShowReleaseNotes()=>CreateReleaseNotes().ShowDialog();

    internal Window CreateReleaseNotes(Func<Uri,CancellationToken,Task<byte[]>>? read=null,Action<Uri>? openBrowser=null)
    {
        read??=(uri,token)=>sourceClient.Read(uri,256*1024,token);
        openBrowser??=uri=>
        {
            try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
            catch(Exception error){Status.Text=AppLanguage.T("Не удалось открыть описание обновления: ")+error.Message;}
        };
        var offeredVersion=updateOffer?.Manifest.Version;
        var source=offeredVersion==null?ReleaseNotesSource.Latest:ReleaseNotesSource.ForVersion(offeredVersion);
        var area=SystemParameters.WorkArea;
        var window=new Window{ShowInTaskbar=false,Owner=this,Title=AppLanguage.T("Что нового"),Width=Math.Min(620,Math.Max(340,area.Width-40)),Height=Math.Min(640,Math.Max(360,area.Height-40)),MinWidth=340,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterOwner,UseLayoutRounding=true,FontFamily=FontFamily,FontSize=13};
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Text");
        var root=new DockPanel{Margin=new(24,20,24,20)};window.Content=root;
        var footer=new DockPanel{Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var close=Button("Закрыть",window.Close);close.Name="ReleaseNotesClose";close.Style=(Style)FindResource("PrimaryButton");close.IsDefault=true;close.IsCancel=true;close.Padding=new(16,7,16,7);DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
        Uri page=ReleaseNotesSource.Page;
        var github=Button("Открыть на GitHub",()=>openBrowser(page));github.Name="ReleaseNotesGitHub";github.Style=(Style)FindResource("QuietButton");github.Padding=new(12,7,12,7);github.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(github);
        var kicker=TextRaw(AppLanguage.T("Что нового"),12);kicker.FontWeight=FontWeights.SemiBold;kicker.Margin=new(0,0,0,4);kicker.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        var headline=TextRaw(AppLanguage.T("Новости Качалки"),23);headline.FontWeight=FontWeights.SemiBold;headline.Margin=new(0,0,0,14);AutomationProperties.SetName(headline,headline.Text);
        var head=new StackPanel();DockPanel.SetDock(head,Dock.Top);head.Children.Add(kicker);head.Children.Add(headline);root.Children.Add(head);
        var scroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(scroll,AppLanguage.T("Что нового"));root.Children.Add(scroll);
        var body=new StackPanel{Name="ReleaseNotesBody"};scroll.Content=body;

        TextBlock Rich(string value,double size,bool muted)
        {
            var block=new TextBlock{FontSize=size,TextWrapping=TextWrapping.Wrap};block.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Text");
            foreach(var run in ReleaseNotesSource.Inline(value))
            {
                var part=new System.Windows.Documents.Run(run.Text);
                if(run.Bold)part.FontWeight=FontWeights.SemiBold;
                if(run.Code){part.FontFamily=(FontFamily)FindResource("MonoFont");part.FontSize=size-1;}
                block.Inlines.Add(part);
            }
            return block;
        }
        void Show(ReleaseNotesDocument notes)
        {
            page=notes.Address;
            kicker.Text=AppLanguage.T("Что нового")+" · "+notes.Version;headline.Text=notes.Title;AutomationProperties.SetName(headline,notes.Title);
            body.Children.Clear();
            foreach(var block in notes.Blocks)
            {
                if(block.Kind==ReleaseNotesKind.Subheading)
                {
                    var title=Rich(block.Text,15,false);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0,10,0,6);body.Children.Add(title);
                }
                else if(block.Kind==ReleaseNotesKind.Bullet)
                {
                    var row=new Grid{Margin=new(0,0,0,9)};row.ColumnDefinitions.Add(new(){Width=new GridLength(20)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                    var dot=new System.Windows.Shapes.Ellipse{Width=6,Height=6,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(2,7,0,0)};dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Accent");row.Children.Add(dot);
                    var text=Rich(block.Text,13.5,false);Grid.SetColumn(text,1);row.Children.Add(text);body.Children.Add(row);
                }
                else
                {
                    var text=Rich(block.Text,13,true);text.Margin=new(0,0,0,9);body.Children.Add(text);
                }
            }
        }
        void Failed()
        {
            body.Children.Clear();
            var message=Text("Не удалось загрузить описание. Проверь соединение или открой страницу выпуска.",13,true);message.Name="ReleaseNotesFailed";body.Children.Add(message);
            var retry=Button("Повторить",async()=>await Load());retry.Style=(Style)FindResource("QuietButton");retry.Padding=new(12,7,12,7);retry.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(retry);
        }
        var cts=new CancellationTokenSource();window.Closed+=(_,_)=>{cts.Cancel();cts.Dispose();};
        async Task Load()
        {
            body.Children.Clear();var loading=Text("Загружаем описание…",13,true);loading.Name="ReleaseNotesLoading";body.Children.Add(loading);
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cts.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var notes=ReleaseNotesSource.Parse(await read(source,timeout.Token));
                if(!cts.IsCancellationRequested)Show(notes);
            }
            catch(Exception error) when(!cts.IsCancellationRequested&&error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException){Failed();}
        }
        window.ContentRendered+=async(_,_)=>await Load();
        return window;
    }
}
