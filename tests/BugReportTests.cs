using System.Text.Json;
using Kachalka;

static class BugReportTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var folder=@"D:\Личное кино\Ka4alka";
        var raw=string.Join('\n',folder,@"C:\Users\Another Person\Desktop\private.txt",@"\\private-server\share\my file.txt","/home/private-user/private.txt","magnet:?xt=urn:btih:private-hash&tr=udp://tracker.test/private-passkey/announce","https://user:private-password@tracker.test/private-passkey/announce?other=private-query", "Authorization: Basic private-authorization", "Cookie: session=private-cookie", "password=\"private quoted password\"",JsonSerializer.Serialize(new{url="https://tracker.test/private-json-passkey/announce",path=@"C:\Users\Unknown\private-json.txt",access_token="private-access-token",client_secret="private-client-secret"}));
        var redacted=DiagnosticReport.Redact(raw,[folder]);
        foreach(var secret in new[]{"Личное кино","Another Person","private-server","private-user","private-hash","private-passkey","private-query","private-password","private-authorization","private-cookie","private quoted password","private-json-passkey","Unknown","private-access-token","private-client-secret"})
            Check(!redacted.Contains(secret,StringComparison.OrdinalIgnoreCase),"bug-report export removes "+secret);
        Check(redacted.Contains("tracker.test")&&redacted.Contains("[скрыто]"),"redacted report retains useful tracker host and visible omission markers");
        Check(DiagnosticReport.Redact(redacted,[folder])==redacted,"redaction is stable when reviewed reports are sanitized again before export");
        var githubToken="ghp_"+new string('A',36);var fineGrained="github_pat_"+new string('b',60);
        var tokenLog=DiagnosticReport.Redact("Unexpected credentials: "+githubToken+" / "+fineGrained,[]);
        Check(!tokenLog.Contains(githubToken)&&!tokenLog.Contains(fineGrained),"recognizable GitHub credentials are removed even without a named secret field");
        var quoted=JsonSerializer.Serialize(new{Nested=new{Error="password=\"private quoted password\"",Message="password='private single quoted password'",Assignments="authorization=\"private multiword secret\"\nCookie='private multiword cookie'",Headers="Cookie: session=private multiword cookie\nAuthorization: Bearer private multiword bearer\nAuthorization: Basic private multiword basic"}});
        var cleanQuoted=DiagnosticReport.Redact(quoted,[]);
        using(var document=JsonDocument.Parse(cleanQuoted))
        {
            var decoded=document.RootElement.GetProperty("Nested").ToString();
            Check(!decoded.Contains("private")&&!decoded.Contains("quoted password")&&!decoded.Contains("multiword"),"decoded nested JSON errors sanitize complete double/single-quoted secrets and multiword authentication headers");
        }
        Check(!DiagnosticReport.Redact("password='private quoted password'",[]).Contains("quoted password"),"plaintext single-quoted secrets are removed completely");
        var assignedHeaders=DiagnosticReport.Redact("authorization=\"private multiword secret\"\nCookie='private multiword cookie'",[]);
        Check(!assignedHeaders.Contains("private")&&!assignedHeaders.Contains("multiword"),"plaintext quoted authorization and cookie assignments are removed completely");
        Dictionary<string,string> Fields(Uri uri)=>uri.Query.TrimStart('?').Split('&').Select(part=>part.Split('=',2)).ToDictionary(parts=>Uri.UnescapeDataString(parts[0]),parts=>Uri.UnescapeDataString(parts.Length>1?parts[1]:""));
        var problem="Загрузка остановилась после сна\nОжидал продолжение — 50 %";var steps="1. Скачать серию\n2. Закрыть крышку\n3. Открыть ноутбук";
        var draft=BugReportDraft.Create(problem,steps,"Версия и наблюдения:\nСеть доступна\nТрекер не ответил");
        Check(draft.FormUri is{Scheme:"https",Host:"github.com",AbsolutePath:"/peregond/ka4alka/issues/new"}&&!draft.RequiresClipboard,"short reports open only the repository's existing GitHub issue form");
        var fields=Fields(draft.FormUri!);
        Check(fields["template"]=="bug_report.yml"&&fields["problem"]==problem+"\n\nКак повторить:\n"+steps&&fields["diagnostic"]==draft.Diagnostics,"prefilled GitHub YAML fields preserve Russian, percent signs and line breaks");
        Check(fields.ContainsKey("version")&&!draft.FormUri!.AbsoluteUri.Contains("&body="),"report uses the real issue form field IDs rather than an ignored body parameter");
        var unsafeDraft=BugReportDraft.Create("Не удалось открыть "+folder,"token=private-user-token",raw,[folder]);
        Check(!unsafeDraft.FullReport.Contains("private-user-token")&&!unsafeDraft.FullReport.Contains(folder)&&!Uri.UnescapeDataString(unsafeDraft.FormUri!.AbsoluteUri).Contains("private-passkey"),"description, steps, diagnostic preview and browser parameters are all sanitized");
        var large="Начало диагностики\n"+new string('я',250_000)+"\nПоследнее наблюдение";
        var overflow=BugReportDraft.Create(problem,steps,large);
        Check(overflow.RequiresClipboard&&!overflow.DescriptionTooLong&&overflow.FormUri!.AbsoluteUri.Length<=BugReportDraft.MaximumUriLength,"large diagnostic logs use a browser-safe link and explicit full-report clipboard fallback");
        var overflowFields=Fields(overflow.FormUri!);
        Check(overflowFields["problem"]==fields["problem"]&&overflow.FullReport.EndsWith("Последнее наблюдение")&&overflowFields["diagnostic"].Contains("буфер обмена"),"overflow never truncates the user's problem or steps and explains where the full report went");
        var enormousProblem=new string('ы',4000);var enormous=BugReportDraft.Create(enormousProblem,steps,"Логи");
        Check(enormous.DescriptionTooLong&&enormous.FormUri==null&&enormous.FullReport.Contains(enormousProblem)&&enormous.FullReport.Contains(steps),"a long description is kept intact for copy/save instead of silently losing critical instructions");
        var emoji=BugReportDraft.Create(new string('x',95)+"🎬"+"название","","Факт");
        Check(!Fields(emoji.FormUri!)["title"].Contains('�'),"title length limit never splits a Unicode surrogate pair");
        Check(BugReportDraft.Create("  ","","Логи").FormUri==null,"empty bug descriptions cannot open an empty issue");
        var allLimitsSafe=true;
        for(var limit=512;limit<=820;limit+=7)
        {
            var bounded=BugReportDraft.Create("Ошибка","",large,maximumUriLength:limit);
            allLimitsSafe&=bounded.FormUri==null||bounded.FormUri.AbsoluteUri.Length<=limit;
        }
        Check(allLimitsSafe,"overflow marker itself respects small URI budgets around the fallback boundary");
        var old=Environment.GetEnvironmentVariable("KACHALKA_DATA");var state=Path.Combine(root,"bug-report-state");Environment.SetEnvironmentVariable("KACHALKA_DATA",state);
        try
        {
            Directory.CreateDirectory(state);await File.WriteAllTextAsync(Path.Combine(state,"diagnostic.log"),new string('x',180_000)+"\nПоследняя полезная запись\n");
            var service=new DownloadService();var prefs=new Preferences{Folder=folder};
            service.Items.Add(new DownloadItem{MediaTitle="Исходная загрузка",Folder=folder});
            var snapshot=DiagnosticReport.Capture(prefs,service);
            prefs.Folder=@"E:\Changed";service.Items.Clear();service.Items.Add(new DownloadItem{MediaTitle="Новая загрузка",Folder=prefs.Folder});
            var report=await Task.Run(()=>DiagnosticReport.Compose(snapshot));
            Check(report.Contains(JsonSerializer.Serialize("Исходная загрузка")[1..^1])&&!report.Contains(JsonSerializer.Serialize("Новая загрузка")[1..^1])&&report.Contains("Последняя полезная запись")&&!service.EngineCreated,"background report reading uses an immutable UI snapshot and does not start an idle torrent engine");
            Check(report.Length<55_000,"report tails stay bounded even when an existing log is large");
            await File.WriteAllTextAsync(Path.Combine(state,"diagnostic.log"),"token="+new string('s',80_000));
            var incomplete=DiagnosticReport.Compose(snapshot);
            Check(!incomplete.Contains(new string('s',40)),"an incomplete log-tail record is omitted when its missing prefix could identify a secret");
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            var observed=false;try{await DiagnosticReport.CreateAsync(prefs,service,canceled.Token);}catch(OperationCanceledException){observed=true;}
            Check(observed,"closing a report can cancel pending background collection");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",old);}
    }
}
