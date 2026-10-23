using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    public async Task CinemaSmokeTest(string output)
    {
        Directory.CreateDirectory(output);await Task.Delay(300);
        liveRequest?.Cancel();searchDelay.Stop();
        var person=new CinemaPerson("Участник UI-проверки","Актёры","",Character:"Персонаж UI-проверки");
        var original=new MediaItem(-70001,"Первый проверочный фильм","Фильмы","Драма",2020,"8.2","7.8","#526B69")
        {
            PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-first",Description="Описание фильма",OriginalTitle="First verification film: a story to remember",
            People=[new("Проверочный режиссёр","Режиссёры",""),person,..Enumerable.Range(2,70).Select(x=>new CinemaPerson("Участник "+x,"Актёры",""))]
        };
        var next=original with{Id=-70002,Title="Второй проверочный фильм",OriginalTitle="Second verification film",PageUrl=LiveCatalog.Base+"/movies/cinema-smoke-second",Year=2022,Kinopoisk="9.1"};
        var biography="Биография участника для проверки интерфейса. "+new string('а',400);
        var folder=Path.Combine(Preferences.DataDir,"people");Directory.CreateDirectory(folder);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.Name+"|"+person.Role)));
        var portraitUrl="https://kino-teatr.ua/public/main/persons/cinema-smoke.jpg";
        var origins=new MediaItem?[]{original,next,null};
        foreach(var (participant,index) in original.People.Select((participant,index)=>(participant,index)))
        {
            var sourceUrl="https://kino-teatr.ua/ru/person/cinema-smoke-participant-"+(70001+index)+".phtml";
            var profile=new ProfessionalPerson(participant with{ProfileUrl=sourceUrl},biography,portraitUrl,sourceUrl,"Kino-Teatr.ua",
                [new(original.Title,original.Year),new(next.Title,next.Year)],[participant.Name]);
            foreach(var origin in origins)
                await File.WriteAllTextAsync(ProfessionalCinemaPeople.CachePath(participant,origin),JsonSerializer.Serialize(profile));
            if(participant==person)
                await File.WriteAllTextAsync(Path.Combine(folder,key+".json"),JsonSerializer.Serialize(new PersonProfile(person,biography,[original,next],sourceUrl)));
            var portraitKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(participant.Name)));
            await File.WriteAllTextAsync(Path.Combine(folder,portraitKey+".portrait.json"),JsonSerializer.Serialize(portraitUrl));
        }
        var portraitDir=Path.Combine(Preferences.DataDir,"portraits");Directory.CreateDirectory(portraitDir);
        var portrait=new RenderTargetBitmap(20,30,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(Brushes.Teal,null,new Rect(0,0,20,30));portrait.Render(drawing);
        var portraitEncoder=new PngBitmapEncoder();portraitEncoder.Frames.Add(BitmapFrame.Create(portrait));using(var stream=File.Create(Path.Combine(portraitDir,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(portraitUrl)))+".img")))portraitEncoder.Save(stream);
        requestedDetails.Add(original.Id);requestedDetails.Add(next.Id);liveReleases[original.Id]=[];liveReleases[next.Id]=[];
        cardMetadata[original.Id]=Task.FromResult(original);cardMetadata[next.Id]=Task.FromResult(next);
        section="Фильмы";current=original;Render();UpdateLayout();
        var open=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Открыть карточку: "+person.Name+", "+person.CreditLabel)??throw new Exception("Person action missing.");
        open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var limit=DateTime.UtcNow.AddSeconds(15);
        while((FindVisual<TextBlock>(Body,x=>x.Text==biography)==null||FindVisual<Image>(Body,x=>x.Source!=null)==null)&&DateTime.UtcNow<limit)await Task.Delay(100);
        if(FindVisual<TextBlock>(Body,x=>x.Text==biography)==null)throw new Exception("Person page did not load cached biography.");
        var biographyNode=FindVisual<TextBlock>(Body,x=>x.Text==biography);var biographyRequest=personRequest;
        RefreshDetail(original.Id);
        if(!ReferenceEquals(biographyNode,FindVisual<TextBlock>(Body,x=>x.Text==biography))||!ReferenceEquals(biographyRequest,personRequest))throw new Exception("Movie-source progress restarted the open person page.");
        if(Application.Current.Windows.OfType<Window>().Any(x=>x.Owner==this&&x.Title==person.Name))throw new Exception("Person navigation opened a popup.");
        void Shot(string name)
        {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
        }
        if(FindVisual<Image>(Body,x=>x.Source!=null)==null)
        {
            var missing=VisualElements<Image>(Body).Select(x=>new{Photo=AutomationProperties.GetName(x),x.IsLoaded,x.ActualWidth,x.ActualHeight,Bounds=x.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),x.RenderSize)),Tooltip=(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(x)) as FrameworkElement)?.ToolTip?.ToString()});
            await File.WriteAllTextAsync(Path.Combine(output,"portrait-missing.json"),JsonSerializer.Serialize(missing));throw new Exception("Cached person photograph missing: "+JsonSerializer.Serialize(missing));
        }
        designFixedViewport=true;MaxWidth=2000;MaxHeight=1200;MinWidth=1440;Width=1440;Height=900;await Task.Delay(150);Shot("person-wide");AssertPersonPageLayout(false);AssertPersonFilmGrid();var stallStart=(hero:heroLayouts,bio:biographyUpdates,resize:windowResizes,compact:compactLayouts);var narrowDone=false;
        var layoutPasses=0;var operations=new System.Collections.Concurrent.ConcurrentDictionary<string,int>();var stallDiagnostics=new LayoutStallDiagnostics(this);
        LayoutUpdated+=(_,_)=>Interlocked.Increment(ref layoutPasses);
        Dispatcher.Hooks.OperationCompleted+=(_,args)=>operations.AddOrUpdate(args.Operation.Priority.ToString(),1,(_,count)=>count+1);
        new Thread(()=>{Thread.Sleep(20000);if(Volatile.Read(ref narrowDone))return;try{File.WriteAllText(Path.Combine(output,"error.txt"),
            $"Narrow person layout never settled: hero layouts {heroLayouts-stallStart.hero}, biography updates {biographyUpdates-stallStart.bio}, window resizes {windowResizes-stallStart.resize}, compact layouts {compactLayouts-stallStart.compact}, last hero layout {heroLayoutTrace}, window layout passes {Volatile.Read(ref layoutPasses)}, dispatcher operations {string.Join(", ",operations.Select(x=>x.Key+" "+x.Value))}"+(File.Exists(Path.Combine(Preferences.DataDir,"error.log"))?"\nError log:\n"+string.Concat(File.ReadAllText(Path.Combine(Preferences.DataDir,"error.log")).TakeLast(3000)):"")+"\nDiagnostics:\n"+stallDiagnostics.Report());}catch(IOException){}}){IsBackground=true}.Start();
        MinWidth=620;Width=620;Height=720;await Task.Delay(150);
        // The caption-less window finishes a resize over more than one dispatcher turn; let its layout settle.
        for(var pass=0;pass<3;pass++)
        {
            UpdateLayout();var settled=Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle).Task;
            if(await Task.WhenAny(settled,Task.Delay(5000))!=settled)throw new Exception("Narrow person layout never settled after the shrink to 620 px\n"+stallDiagnostics.Report());
        }
        Volatile.Write(ref narrowDone,true);Shot("person-narrow");AssertPersonPageLayout(true);
        // Every width around the poster and narrow/wide thresholds must settle. A column count or a narrow/wide switch that
        // followed the page's own scroll bar used to loop forever at one width (the 620 px client of a caption-less window).
        MinWidth=540;var unsettled=new List<double>();
        for(var width=560d;width<=760;width+=6)
        {
            Width=width;
            var idle=Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle).Task;
            if(await Task.WhenAny(idle,Task.Delay(3000))!=idle)unsettled.Add(width);
        }
        if(unsettled.Count>0)throw new Exception("Person page layout never settled at window widths "+string.Join(", ",unsettled)+"\n"+stallDiagnostics.Report());
        {
            // Widths and heights around the person page's thresholds, each reached from the oversized wide state. With
            // widths that followed the page's own scroll bar, 632-636 x 740-800 never settled (Windows CI experiment).
            var stalls=new List<string>();var points=0;
            for(var height=700d;height<=800;height+=20)
            for(var width=612d;width<=652;width+=4)
            {
                MinWidth=1440;Width=1440;Height=900;
                var wide=Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle).Task;await Task.WhenAny(wide,Task.Delay(3000));
                MinWidth=540;Width=width;Height=height;points++;
                var idle=Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle).Task;
                if(await Task.WhenAny(idle,Task.Delay(3000))!=idle)stalls.Add($"{width}x{height}");
            }
            if(stalls.Count>0)throw new Exception($"Person page layout never settled at {stalls.Count} of {points} sizes: "+string.Join(", ",stalls)+"\n"+stallDiagnostics.Report());
        }
        MinWidth=620;Width=620;Height=720;await Task.Delay(150);UpdateLayout();AssertPersonPageLayout(true);
        var film=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Открыть "+next.Title)??throw new Exception("Filmography action missing.");
        film.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(current?.Id!=next.Id||activePerson!=null)throw new Exception("Film → person → film navigation failed.");
        CinemaBack();if(activePerson!=person)throw new Exception("Movie back did not restore person page.");
        var back=FindVisual<Button>(HeaderArea,x=>AutomationProperties.GetName(x).Contains("Назад к фильму")||FindVisual<TextBlock>(x,t=>t.Text.StartsWith("Назад к фильму"))!=null)??throw new Exception("Person back action missing.");
        back.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(activePerson!=null||current?.Id!=original.Id)throw new Exception("Person back did not restore original movie.");
        MinWidth=1600;Width=1600;await Task.Delay(150);UpdateLayout();Shot("film-wide");
        var cards=FindVisual<Grid>(Body,x=>x.Name=="CinemaCards")??throw new Exception("Film cards missing.");
        var participants=FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")??throw new Exception("Participants card missing.");
        var crew=FindVisual<System.Windows.Controls.Primitives.UniformGrid>(participants,x=>x.Name=="CinemaPeopleGrid")??throw new Exception("Crew portrait grid missing.");
        if(crew.Columns<4)throw new Exception("The full-width participant card does not use its width for several portraits per row.");
        var title=FindVisual<TextBlock>(Body,x=>x.Name=="DetailTitle")!;var favorite=FindVisual<Button>(Body,x=>x.Name=="DetailFavorite")!;
        if(favorite.TransformToAncestor(Body).Transform(new Point()).Y<title.TransformToAncestor(Body).Transform(new Point()).Y+title.ActualHeight)throw new Exception("Save action must follow the movie title.");
        void CheckIdentityOrder()
        {
            var identity=FindVisual<StackPanel>(Body,x=>x.Name=="DetailIdentity")??throw new Exception("Movie identity is missing.");
            var heading=FindVisual<TextBlock>(identity,x=>x.Name=="DetailTitle")!;
            var originalTitle=FindVisual<TextBlock>(identity,x=>x.Name=="DetailOriginalTitle")??throw new Exception("Original movie title is missing.");
            var save=FindVisual<Button>(identity,x=>x.Name=="DetailFavorite")!;
            var actions=FindVisual<Panel>(identity,x=>x.Name=="DetailActions")??throw new Exception("Movie actions row is missing.");
            if(identity.Children.IndexOf(originalTitle)!=identity.Children.IndexOf(heading)+1||!identity.Children.Contains(actions)||identity.Children.IndexOf(actions)<=identity.Children.IndexOf(originalTitle)||!actions.Children.Contains(save))throw new Exception("Movie identity must be title, original title, details and then the actions row with Save.");
            if(originalTitle.TransformToAncestor(identity).Transform(new Point()).Y<heading.TransformToAncestor(identity).Transform(new Point()).Y+heading.ActualHeight-.5||save.TransformToAncestor(identity).Transform(new Point()).Y<originalTitle.TransformToAncestor(identity).Transform(new Point()).Y+originalTitle.ActualHeight-.5)throw new Exception("Movie title, original title and the actions overlap.");
        }
        void CheckCrewTiles()
        {
            var frame=FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")!;
            // Tiles behind "Ещё" are collapsed and not laid out yet; the visible row carries the checks.
            var tiles=VisualElements<Button>(frame).Where(x=>x.Name=="CinemaPersonTile"&&x.IsVisible).ToArray();
            if(tiles.Length==0)throw new Exception("No crew tile is visible.");
            foreach(var button in tiles)
            {
                if(button.BorderThickness!=new Thickness(0)||button.Background is not SolidColorBrush{Color.A:0})throw new Exception("Crew actions must not draw separate permanent cards.");
                var portrait=FindVisual<Border>(button,x=>x.Name=="CinemaPersonPortrait")??throw new Exception("Crew photograph frame is missing.");
                if(portrait.BorderThickness!=new Thickness(0)||portrait.Clip is not RectangleGeometry{RadiusX:>0,RadiusY:>0})throw new Exception("Crew photographs must keep rounded clipping without an extra outline.");
                if(Math.Abs(portrait.ActualHeight-portrait.ActualWidth*1.25)>2||portrait.ActualWidth<button.ActualWidth-button.Padding.Left-button.Padding.Right-6)throw new Exception("Crew photographs must fill their tile at the Mac 4:5 portrait proportion.");
                var bounds=button.TransformToAncestor(frame).TransformBounds(new Rect(new Point(),button.RenderSize));
                if(bounds.Left<-.5||bounds.Right>frame.ActualWidth+.5)throw new Exception("Crew action overflows its participant card.");
                if(button.Tag is not CinemaPerson person||AutomationProperties.GetName(button)!="Открыть карточку: "+person.Name+", "+person.CreditLabel)throw new Exception("Crew action lost its participant navigation.");
            }
        }
        CheckIdentityOrder();CheckCrewTiles();
        foreach(var link in VisualElements<Button>(detailInfo!).Where(x=>x.Name=="DetailCreditLink"))
            if(link.Tag is not CinemaPerson linked||!AutomationProperties.GetName(link).StartsWith("Открыть страницу: "+linked.Name,StringComparison.Ordinal)||!link.IsEnabled)throw new Exception("A director or cast name in the banner must be a working link to that person's page.");
        var filmCard=FindVisual<Border>(Body,x=>x.Name=="CinemaFilm")!;var hero=detailHero!;
                var ratings=FindVisual<WrapPanel>(filmCard,x=>x.Name=="DetailRatings")!;var year=FindVisual<TextBlock>(filmCard,x=>x.Name=="DetailYear")!;
        if(FindVisual<Button>(filmCard,x=>x.Name=="DetailHide")==null)throw new Exception("The film card needs the eye that hides the title from the catalog.");
        if(!ReferenceEquals(ratings.Parent,detailMetaRow)||ratings.Children.OfType<Border>().Any(x=>x.BorderThickness!=new Thickness(0)))throw new Exception("Ratings must share the metadata line without separate outlined boxes.");
        if(detailSynopsis!.ActualWidth<=0||!ReferenceEquals(detailDescription!.Parent,detailInfo))throw new Exception("The synopsis must sit in the banner's text column.");
        var wasSaved=IsSaved(original);favorite.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(IsSaved(original)==wasSaved||!ReferenceEquals(participants,FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants"))||AutomationProperties.GetName(favorite)!=(IsSaved(original)?"Сохранено":"Сохранить"))throw new Exception("Save must update its state without restarting participant photographs.");
        favorite.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));if(IsSaved(original)!=wasSaved)throw new Exception("Save did not restore its previous state.");
        if(filmCard.ActualHeight-hero.ActualHeight>64)throw new Exception("Movie card leaves excess blank space below its content.");
        if(Grid.GetColumn(participants)!=0||Grid.GetRow(participants)!=1)throw new Exception($"Participants must sit below the banner at full width: window={ActualWidth}, cards={cards.ActualWidth}.");
        void CheckAlignedCrew()
        {
            if(Math.Abs(filmCard.ActualWidth-participants.ActualWidth)>1)throw new Exception("Wide film and participant cards must have the same width.");
            if(participants.TransformToAncestor(cards).Transform(new Point()).Y<filmCard.ActualHeight)throw new Exception("The participant card must start below the banner.");
            if(filmCard.ActualHeight-detailHero!.ActualHeight>64)throw new Exception("Participants stretched the movie card beyond its natural content height.");
        }
        CheckAlignedCrew();
        // A long crew list shows one row of portraits; "Ещё" opens the rest in place, with no scroll area inside the page.
        int VisibleCrew()=>VisualElements<Button>(participants).Count(x=>x.Name=="CinemaPersonTile"&&x.IsVisible);
        if(FindVisual<ScrollViewer>(participants,_=>true)!=null)throw new Exception("The participant card must not scroll inside the page.");
        var crewMore=FindVisual<Button>(participants,x=>x.Name=="CinemaPeopleMore")??throw new Exception("A long crew list must offer \"Ещё\".");
        var crewTotal=VisualElements<Button>(participants).Count(x=>x.Name=="CinemaPersonTile");
        if(!crewMore.IsVisible||VisibleCrew()!=crew.Columns||crewMore.Content?.ToString()!="Ещё "+(crewTotal-crew.Columns))throw new Exception($"A long crew list must show exactly one row with \"Ещё\" for the rest (visible {VisibleCrew()}, columns {crew.Columns}, total {crewTotal}).");
        var oneRow=participants.ActualHeight;
        crewMore.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(100);UpdateLayout();CheckAlignedCrew();
        if(VisibleCrew()!=crewTotal||crewMore.Content?.ToString()!="Свернуть"||participants.ActualHeight<=oneRow)throw new Exception("\"Ещё\" must reveal every participant in place.");
        crewMore.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(100);UpdateLayout();
        if(VisibleCrew()!=crew.Columns)throw new Exception("\"Свернуть\" must return to one row of participants.");
        var heading=FindVisual<TextBlock>(participants,x=>x.Name=="CinemaPeopleHeading")!;
        if(heading.TransformToAncestor(participants).Transform(new Point()).Y<0)throw new Exception("Participant heading must stay visible.");
        current=original with{Description=string.Join(" ",Enumerable.Repeat("Развёрнутое описание для проверки высоты карточки фильма.",30))};descriptionExpanded=false;Render();await Task.Delay(150);UpdateLayout();
        cards=FindVisual<Grid>(Body,x=>x.Name=="CinemaCards")!;filmCard=FindVisual<Border>(Body,x=>x.Name=="CinemaFilm")!;participants=FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")!;
        var collapsedHeight=filmCard.ActualHeight;var expand=FindVisual<Button>(Body,x=>x.Name=="DescriptionToggle")!;
        if(!expand.IsVisible)throw new Exception("A long synopsis must offer an expansion action.");
        expand.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(150);UpdateLayout();
        if(!descriptionExpanded||!double.IsPositiveInfinity(detailSynopsis!.MaxHeight)||filmCard.ActualHeight<=collapsedHeight||!ReferenceEquals(participants,FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")))throw new Exception("Synopsis expansion must reveal the text without recreating participant photographs.");
        CheckAlignedCrew();Shot("film-wide-expanded");
        current=original;descriptionExpanded=false;Render();await Task.Delay(100);UpdateLayout();
        cards=FindVisual<Grid>(Body,x=>x.Name=="CinemaCards")!;filmCard=FindVisual<Border>(Body,x=>x.Name=="CinemaFilm")!;participants=FindVisual<Border>(Body,x=>x.Name=="CinemaParticipants")!;
        CheckAlignedCrew();
        MinWidth=760;Width=760;await Task.Delay(150);UpdateLayout();Shot("film-narrow");
        if(Grid.GetRow(participants)!=1||Grid.GetColumn(participants)!=0)throw new Exception("Narrow layout did not stack participants.");
        if(!double.IsNaN(participants.Height))throw new Exception("Stacked participant card kept its wide-layout height binding.");
        CheckIdentityOrder();CheckCrewTiles();
        crew=FindVisual<System.Windows.Controls.Primitives.UniformGrid>(participants,x=>x.Name=="CinemaPeopleGrid")!;
        if(VisualElements<Button>(participants).Count(x=>x.Name=="CinemaPersonTile"&&x.IsVisible)!=crew.Columns)throw new Exception("Stacked participant list must still show one row until \"Ещё\".");
        MinWidth=620;Width=620;await Task.Delay(150);UpdateLayout();Shot("film-minimum");
        foreach(var element in VisualElements<FrameworkElement>(Body).Where(x=>x.IsVisible&&(x.Name is "DetailTitle" or "DetailOriginalTitle" or "DetailTraits" or "DetailRatings" or "DetailFavorite" or "DetailSynopsis")))
        {
            var bounds=element.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),element.RenderSize));if(bounds.Right>Body.ActualWidth+1||bounds.Left<-.5)throw new Exception("Minimum-width detail element overflows: "+element.Name);
        }
        CheckIdentityOrder();CheckCrewTiles();
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{PersonCard=true,Biography=true,Filmography=true,WideAndNarrow=true,Navigation=true,TitleOrder=true,QuietCrewTiles=true,InlineSave=true,FullWidthSynopsis=true}));Close();
    }
}
