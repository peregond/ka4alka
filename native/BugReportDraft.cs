using System.Text;

namespace Kachalka;

public sealed record BugReportDraft(string Title,string Problem,string Diagnostics,string FullReport,Uri? FormUri,bool RequiresClipboard,bool DescriptionTooLong)
{
    public const int MaximumUriLength=8000;
    const string DiagnosticOverflow="Полный очищенный отчёт скопирован в буфер обмена. Вставь его в поле диагностики перед отправкой. Ниже — начало отчёта.";

    public static BugReportDraft Create(string problem,string steps,string diagnostics,IEnumerable<string>? privatePaths=null,int maximumUriLength=MaximumUriLength)
    {
        if(maximumUriLength<512||maximumUriLength>MaximumUriLength)throw new ArgumentOutOfRangeException(nameof(maximumUriLength));
        var paths=(privatePaths??[]).ToArray();
        string Clean(string value)=>DiagnosticReport.Redact(value.Replace("\r\n","\n").Replace('\r','\n').Replace("\0",""),paths).Trim();
        problem=Clean(problem);steps=Clean(steps);diagnostics=Clean(diagnostics);
        var details=problem+(steps.Length==0?"":"\n\nКак повторить:\n"+steps);
        var firstLine=problem.Split('\n')[0];var title="[Ошибка] "+Prefix(firstLine,96);
        var version=(typeof(BugReportDraft).Assembly.GetName().Version?.ToString(3)??"неизвестна")+" · "+Environment.OSVersion.VersionString;
        var full=new StringBuilder().AppendLine("## Что произошло?").AppendLine(details).AppendLine().AppendLine("## Версия приложения и Windows").AppendLine(version).AppendLine().AppendLine("## Диагностический отчёт").Append(diagnostics).ToString();
        var baseLink=DiagnosticReport.IssueUrl+"&title="+Uri.EscapeDataString(title)+"&problem="+Uri.EscapeDataString(details)+"&version="+Uri.EscapeDataString(version)+"&diagnostic=";
        string Build(string diagnostic)=>baseLink+Uri.EscapeDataString(diagnostic);
        if(problem.Length==0)return new(title,details,diagnostics,full,null,false,false);
        // Avoid constructing an oversized Uri (some browser/runtime versions
        // reject it) or percent-encoding hundreds of kilobytes unnecessarily.
        if(diagnostics.Length<=maximumUriLength)
        {
            var complete=Build(diagnostics);
            if(complete.Length<=maximumUriLength)return new(title,details,diagnostics,full,new(complete),false,false);
        }
        var minimum=Build(DiagnosticOverflow);
        // Never discard the user's problem or steps just to fit a browser URL.
        // They can instead save/copy the complete reviewed report explicitly.
        if(minimum.Length>maximumUriLength)return new(title,details,diagnostics,full,null,true,true);
        var low=0;var high=Math.Min(diagnostics.Length,maximumUriLength);
        while(low<high)
        {
            var middle=low+(high-low+1)/2;
            if(Build(DiagnosticOverflow+"\n\n"+Prefix(diagnostics,middle)).Length<=maximumUriLength)low=middle;
            else high=middle-1;
        }
        var shortened=Build(DiagnosticOverflow+(low==0?"":"\n\n"+Prefix(diagnostics,low)));
        return new(title,details,diagnostics,full,new(shortened),true,false);
    }
    static string Prefix(string value,int length)
    {
        length=Math.Min(value.Length,length);
        if(length>0&&length<value.Length&&char.IsHighSurrogate(value[length-1])&&char.IsLowSurrogate(value[length]))length--;
        return value[..length];
    }
}
