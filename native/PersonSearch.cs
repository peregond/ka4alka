using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Text.RegularExpressions;

namespace Kachalka;

public partial class MainWindow
{
    internal Func<string,CancellationToken,Task<IReadOnlyList<CinemaPerson>>>? peopleSearchProvider;
    CinemaPerson[] peopleSearchItems=[];
    readonly Dictionary<string,(DateTime SavedUtc,CinemaPerson[] Items)> peopleSearchCache=[];
    CancellationTokenSource? peopleSearchRequest;
    string peopleSearchQuery="",peopleSearchResolvedQuery="",peopleSearchError="";
    bool PeopleOnlySearch=>SearchActive&&searchCategory=="Люди";
    bool PeopleSearchLoading=>peopleSearchRequest is {IsCancellationRequested:false};
    record PersonSearchReturn(string Section,string LastSection,string Query,string Category,int Page);
    PersonSearchReturn? personSearchReturn;

    static string PersonSearchTerm(string value)=>Regex.Replace(value.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{N}]+"," ").Trim();
    static bool SearchablePerson(CinemaPerson person)=>person.Name.Length is >=3 and <=160&&
        Regex.IsMatch(person.Name,@"\p{L}.*\p{L}")&&person.Role is "Актёры" or "Режиссёры" or "Операторы";
    static bool PersonSearchMatches(CinemaPerson person,string term)
    {
        if(!SearchablePerson(person)||term.Length<2)return false;
        var name=PersonSearchTerm(person.Name);
        return term.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(token=>name.Contains(token,StringComparison.Ordinal));
    }
    static CinemaPerson[] MergePeopleSearch(string term,IEnumerable<CinemaPerson> first,IEnumerable<CinemaPerson> second)
    {
        var candidates=first.Concat(second).Where(person=>PersonSearchMatches(person,term)).ToArray();
        var linkedPages=candidates.Where(person=>!string.IsNullOrWhiteSpace(person.ProfileUrl)&&!string.IsNullOrWhiteSpace(person.PageUrl)).Select(person=>(person.Role,person.PageUrl)).ToHashSet();
        var profileIds=candidates.Where(person=>!string.IsNullOrWhiteSpace(person.ProfileUrl)&&!string.IsNullOrWhiteSpace(person.SourcePersonId)).Select(person=>(person.Role,person.SourcePersonId)).ToHashSet();
        var pageIds=candidates.Where(person=>!string.IsNullOrWhiteSpace(person.PageUrl)&&!string.IsNullOrWhiteSpace(person.SourcePersonId)).Select(person=>(person.Role,person.SourcePersonId)).ToHashSet();
        return candidates.GroupBy(person=>PersonSearchTerm(person.Name)+"|"+person.Role).SelectMany(PeopleSearchIdentities)
            .Where(person=>!string.IsNullOrWhiteSpace(person.ProfileUrl)||
                (!string.IsNullOrWhiteSpace(person.PageUrl)?!linkedPages.Contains((person.Role,person.PageUrl))&&!profileIds.Contains((person.Role,person.SourcePersonId)):
                 !string.IsNullOrWhiteSpace(person.SourcePersonId)?!profileIds.Contains((person.Role,person.SourcePersonId))&&!pageIds.Contains((person.Role,person.SourcePersonId)):true))
            .OrderByDescending(person=>PersonSearchTerm(person.Name)==term).ThenBy(person=>person.Name,StringComparer.CurrentCultureIgnoreCase)
            .DistinctBy(PeopleSearchIdentity).Take(40).ToArray();
    }
    static string PeopleSearchIdentity(CinemaPerson person)=>person.Role+"|"+
        (!string.IsNullOrWhiteSpace(person.ProfileUrl)?"profile:"+person.ProfileUrl:!string.IsNullOrWhiteSpace(person.PageUrl)?"catalog:"+person.PageUrl:
         !string.IsNullOrWhiteSpace(person.SourcePersonId)?"zona-id:"+person.SourcePersonId:"name:"+PersonSearchTerm(person.Name));
    static IEnumerable<CinemaPerson> PeopleSearchIdentities(IEnumerable<CinemaPerson> group)
    {
        // Shared names do not identify people. Explicit URLs and verified
        // catalog IDs link candidates; unidentified credits cannot choose a namesake.
        var people=group.ToArray();
        var profiles=people.Where(person=>!string.IsNullOrWhiteSpace(person.ProfileUrl)).GroupBy(person=>person.ProfileUrl!,StringComparer.Ordinal).ToArray();
        var pages=people.Where(person=>string.IsNullOrWhiteSpace(person.ProfileUrl)&&!string.IsNullOrWhiteSpace(person.PageUrl)).GroupBy(person=>person.PageUrl,StringComparer.Ordinal).ToArray();
        var ids=people.Where(person=>string.IsNullOrWhiteSpace(person.ProfileUrl)&&string.IsNullOrWhiteSpace(person.PageUrl)&&!string.IsNullOrWhiteSpace(person.SourcePersonId)).GroupBy(person=>person.SourcePersonId!,StringComparer.Ordinal).ToArray();
        foreach(var profile in profiles)yield return profile.FirstOrDefault(person=>!string.IsNullOrWhiteSpace(person.PageUrl))??profile.First();
        foreach(var page in pages)yield return page.First();
        foreach(var id in ids)yield return id.First();
        if(profiles.Length==0&&pages.Length==0&&ids.Length==0)yield return people[0];
    }
    CinemaPerson[] KnownSearchPeople(string term)=>MergePeopleSearch(term,
        liveItems.Concat(prefs.LiveFavorites).Concat(catalogIndex.Recent("Фильмы",200)).Concat(catalogIndex.Recent("Сериалы",200))
        .SelectMany(item=>item.People),[]);

    void CancelPeopleSearch(bool clear=false)
    {
        var pending=peopleSearchRequest;peopleSearchRequest=null;pending?.Cancel();
        if(clear){peopleSearchQuery="";peopleSearchResolvedQuery="";peopleSearchItems=[];peopleSearchError="";}
    }
    void StartPeopleSearch()
    {
        if(!SearchActive||current!=null||activePerson!=null||section is not ("Фильмы" or "Сериалы")){CancelPeopleSearch();return;}
        var query=submittedQuery;var term=PersonSearchTerm(query);
        if(term.Length<2){CancelPeopleSearch(true);return;}
        var local=KnownSearchPeople(term);
        if(peopleSearchQuery==term)peopleSearchItems=MergePeopleSearch(term,peopleSearchItems,local);
        else{CancelPeopleSearch(true);peopleSearchQuery=term;peopleSearchItems=local;}
        if(PeopleSearchLoading||peopleSearchResolvedQuery==term)return;
        if(peopleSearchCache.TryGetValue(term,out var saved)&&saved.SavedUtc>DateTime.UtcNow.AddMinutes(-20))
        {peopleSearchItems=MergePeopleSearch(term,saved.Items,local);peopleSearchResolvedQuery=term;return;}
        var request=peopleSearchRequest=new CancellationTokenSource(TimeSpan.FromSeconds(16));
        _=FindPeople();
        async Task FindPeople()
        {
            try
            {
                await Task.Yield();
                IReadOnlyList<CinemaPerson> remote=peopleSearchProvider!=null?
                    await peopleSearchProvider(query,request.Token).WaitAsync(request.Token):
                    await new ProfessionalCinemaPeople(sourceClient).SearchPeople(query,request.Token).WaitAsync(request.Token);
                if(request.IsCancellationRequested||!ReferenceEquals(peopleSearchRequest,request))return;
                peopleSearchItems=MergePeopleSearch(term,remote,KnownSearchPeople(term));
                if(peopleSearchCache.Count>=40)peopleSearchCache.Remove(peopleSearchCache.MinBy(pair=>pair.Value.SavedUtc).Key);
                peopleSearchCache[term]=(DateTime.UtcNow,peopleSearchItems);peopleSearchError="";
            }
            catch(OperationCanceledException)
            {
                if(ReferenceEquals(peopleSearchRequest,request))peopleSearchError="Поиск участников не ответил вовремя.";
            }
            catch(Exception)
            {
                if(ReferenceEquals(peopleSearchRequest,request))peopleSearchError="Поиск участников сейчас недоступен.";
            }
            finally
            {
                var active=ReferenceEquals(peopleSearchRequest,request);
                if(active){peopleSearchRequest=null;peopleSearchResolvedQuery=term;}
                request.Dispose();
                if(active&&!closed&&current==null&&activePerson==null&&SearchActive&&submittedQuery==query&&(section is "Фильмы" or "Сериалы"))RenderCatalogKeepingPosition();
            }
        }
    }
    void RetryPeopleSearch()
    {
        CancelPeopleSearch();peopleSearchResolvedQuery="";peopleSearchError="";peopleSearchCache.Remove(PersonSearchTerm(submittedQuery));Render();
    }
    void OpenSearchPerson(CinemaPerson person)
    {
        personSearchReturn=new(section,lastCatalogSection,submittedQuery,searchCategory,livePage);
        OpenPerson(person,null);
    }
    void RestorePersonSearch()
    {
        activePerson=null;personOrigin=null;returnPerson=null;current=null;
        if(personSearchReturn is {} saved)
        {
            section=saved.Section;lastCatalogSection=saved.LastSection;submittedQuery=saved.Query;searchCategory=saved.Category;livePage=saved.Page;
            Search.Text=saved.Query;liveKey=CurrentCatalogKey;
        }
        personSearchReturn=null;Render();
    }
    void RenderPeopleSearchResults(StackPanel parent,bool full)
    {
        if(!SearchActive||!full&&searchCategory!=""||!full&&peopleSearchItems.Length==0&&!PeopleSearchLoading)return;
        var heading=Text("Люди",20);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,10);parent.Children.Add(heading);
        if(PeopleSearchLoading){var loading=Text("Ищем актёров, режиссёров и операторов…",12,true);AutomationProperties.SetName(loading,"Поиск участников");parent.Children.Add(loading);}
        var row=new WrapPanel{Margin=new(0,0,0,12)};parent.Children.Add(row);
        foreach(var person in peopleSearchItems.Take(full?40:6))
        {
            var button=Button(person.Name,()=>OpenSearchPerson(person));button.Tag=person;button.Style=(Style)FindResource("PillButton");
            button.Width=144;button.Padding=new(10);button.Margin=new(0,0,12,12);button.VerticalContentAlignment=VerticalAlignment.Top;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var tile=new StackPanel();tile.Children.Add(PersonPortrait(person,112,140));
            var name=Text(person.Name,13);name.FontWeight=FontWeights.SemiBold;name.TextAlignment=TextAlignment.Center;name.MaxHeight=38;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new(0,0,0,5);tile.Children.Add(name);
            var role=Text(person.Role,11,true);role.TextAlignment=TextAlignment.Center;role.Margin=new(0);tile.Children.Add(role);button.Content=tile;
            AutomationProperties.SetName(button,"Открыть участника "+person.Name);button.ToolTip=person.Name;row.Children.Add(button);
        }
        if(full&&peopleSearchItems.Length==0&&!PeopleSearchLoading)
            parent.Children.Add(Text(peopleSearchError.Length>0?peopleSearchError:"Участники не найдены. Попробуй указать имя и фамилию.",13,true));
        if(full&&peopleSearchError.Length>0){var retry=ActionButton("Повторить поиск людей","IconRefresh",RetryPeopleSearch);retry.HorizontalAlignment=HorizontalAlignment.Left;parent.Children.Add(retry);}
        if(!full&&peopleSearchItems.Length>6){var more=Button("Все участники · "+peopleSearchItems.Length,()=>{searchCategory="Люди";livePage=1;catalogLastPage=null;Render();});more.Style=(Style)FindResource("QuietButton");more.HorizontalAlignment=HorizontalAlignment.Left;parent.Children.Add(more);}
    }
}
