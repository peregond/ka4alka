using Kachalka;

static class FeatureSummaryTests
{
    public static void Run()
    {
        void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        Check(FeatureSummary.Shorten("<p>История <b>друзей</b> &amp; приключений.</p>\n  Новый мир.")=="История друзей & приключений. Новый мир.","banner descriptions collapse markup, entities and whitespace");
        foreach(var empty in new string?[]{null," ","Описание пока недоступно.","Описание временно недоступно.","Загружаем описание…"})
            Check(FeatureSummary.Shorten(empty)=="","missing descriptions never become marketing copy or loading text");
        var summary=FeatureSummary.Shorten(string.Concat(Enumerable.Repeat("Герои отправляются в путешествие, которое изменит их жизнь. ",20)));
        Check(summary.Length<=241&&summary.EndsWith('…'),"long descriptions remain a short bounded teaser");
        var unicode=FeatureSummary.Shorten(new string('я',239)+"🎬"+new string('я',50));
        Check(unicode.EndsWith('…')&&!unicode.Any(char.IsSurrogate),"summary truncation does not split a four-byte character");
        Check(FeatureSummary.Shorten("Короткая история.")=="Короткая история.","short real descriptions retain their punctuation");
    }
}
