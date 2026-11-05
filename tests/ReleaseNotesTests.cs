using System.IO;
using System.Text;
using System.Text.Json;
using Kachalka;

static class ReleaseNotesTests
{
    static byte[] Release(Action<Dictionary<string,object?>>? change=null)
    {
        var item=new Dictionary<string,object?>
        {
            ["tag_name"]="v9.9.9",["name"]="Качалка 9.9.9",["draft"]=false,["prerelease"]=false,
            ["html_url"]="https://github.com/peregond/ka4alka/releases/tag/v9.9.9",
            ["body"]="# Качалка 9.9.9 — заголовок\r\n\r\n- **Windows:** новое `окно`\r\n- Второй пункт\r\n\r\nТекст из\r\nдвух строк."
        };
        change?.Invoke(item);
        return JsonSerializer.SerializeToUtf8Bytes(item);
    }

    public static void Run()
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        var notes=ReleaseNotesSource.Parse(Release());
        Check(notes.Version=="9.9.9"&&notes.Title=="Качалка 9.9.9 — заголовок","the first heading of the notes becomes the title of the window");
        Check(notes.Blocks.Select(x=>x.Kind).SequenceEqual([ReleaseNotesKind.Bullet,ReleaseNotesKind.Bullet,ReleaseNotesKind.Paragraph])&&notes.Blocks[2].Text=="Текст из двух строк.","bullets stay separate and the lines of a paragraph are joined");
        var runs=ReleaseNotesSource.Inline(notes.Blocks[0].Text);
        Check(runs.Count==3&&runs[0]==("Windows:",true,false)&&runs[1]==(" новое ",false,false)&&runs[2]==("окно",false,true),"bold and code inside a line are kept as runs");
        Check(ReleaseNotesSource.Inline("см. [описание](https://example.com/x) здесь").Single().Text=="см. описание здесь","a link keeps its text");
        Check(ReleaseNotesSource.ForVersion("0.45.2").AbsolutePath.EndsWith("/releases/tags/v0.45.2")&&ReleaseNotesSource.ForVersion("../x")==ReleaseNotesSource.Latest,"an offered version asks for its own release and nothing else");
        void Rejected(string label,Action<Dictionary<string,object?>> change)
        {
            var rejected=false;
            try{ReleaseNotesSource.Parse(Release(change));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"release notes are rejected: "+label);
        }
        Rejected("a tag without v",x=>x["tag_name"]="9.9.9");
        Rejected("a draft",x=>x["draft"]=true);
        Rejected("a pre-release",x=>x["prerelease"]=true);
        Rejected("another host",x=>x["html_url"]="https://example.com/peregond/ka4alka/releases/tag/v9.9.9");
        Rejected("another repository",x=>x["html_url"]="https://github.com/other/repo/releases/tag/v9.9.9");
        Rejected("an empty text",x=>x["body"]="  ");
        var tooLarge=false;
        try{ReleaseNotesSource.Parse(Encoding.UTF8.GetBytes("{\"x\":\""+new string('a',300*1024)+"\"}"));}catch(InvalidDataException){tooLarge=true;}
        Check(tooLarge,"an oversized answer is rejected before parsing");
    }
}
