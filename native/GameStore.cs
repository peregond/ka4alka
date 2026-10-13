using System.Globalization;
using System.Text.RegularExpressions;
namespace Kachalka;

// What a Steam search row says about a game besides its name: the price and any discount, the release date and the reviews.
public sealed record GameStoreInfo(string Price="",string OriginalPrice="",int Discount=0,bool Free=false,string Released="",bool Soon=false,int ReviewPercent=0,int ReviewCount=0,string ReviewTone="",string ReviewLabel="")
{
    public bool HasPrice=>Price.Length>0&&!Free;
    public bool HasDiscount=>HasPrice&&Discount>0;
    public string DiscountText=>"−"+Discount+"%";
    public bool HasReview=>ReviewPercent>0&&ReviewTone.Length>0;
    public string ReviewText=>"👍 "+ReviewPercent+"%";
    public string ReviewTip=>(ReviewLabel.Length>0?ReviewLabel+" · ":"")+ReviewPercent+"% положительных отзывов Steam"+(ReviewCount>0?" из "+GameNumbers.Count(ReviewCount):"");
    public string PriceTip=>HasDiscount&&OriginalPrice.Length>0?"Скидка "+Discount+"%. Обычная цена — "+OriginalPrice:"Цена в Steam";
    public string SoonLabel=>Released.Replace(" г.","").Replace("Ещё не объявлена","Дата не объявлена");
    public string SoonTip=>Released.Length>0?"Дата выхода: "+Released:"Дата выхода пока не объявлена";
}
public sealed record GameShot(string Thumb,string Full);
public sealed record GameReview(int Percent,int Count,int Score)
{
    public string Tone=>Score>=6?"positive":Score==5?"mixed":Score>0?"negative":"";
    public string Label=>GameReviewScale.Label(Score);
}
public enum GameRussian { Unknown,Absent,Interface,Voiced }

public static class GameNumbers
{
    // 2 739 409 → "2,7 млн", 31 416 → "31 тыс.", 549 → "549": the exact figure is in the tooltip of Steam itself.
    public static string Count(int value)
    {
        var ru=CultureInfo.GetCultureInfo("ru-RU");
        if(value>=1_000_000)return (value/1_000_000d).ToString("0.#",ru)+" млн";
        if(value>=10_000)return (value/1000)+" тыс.";
        return value.ToString("N0",CultureInfo.InvariantCulture).Replace(',',' ');
    }
    public static int Digits(string text)
    {
        var value=0L;foreach(var symbol in text)if(symbol is >='0' and <='9'){value=value*10+(symbol-'0');if(value>int.MaxValue)return int.MaxValue;}
        return (int)value;
    }
}

public static class GameReviewScale
{
    // Steam's nine labels, in the wording of its Russian store.
    public static string Label(int score)=>score switch
    {
        9=>"Крайне положительные",8=>"Очень положительные",7=>"В основном положительные",6=>"Положительные",5=>"Смешанные",
        4=>"В основном отрицательные",3=>"Отрицательные",2=>"Очень отрицательные",1=>"Крайне отрицательные",_=>""
    };
}

public static class GamePrice
{
    // Steam's search API gives prices as cents of a currency; the store pages give them as ready text.
    public static string Format(string? currency,long cents)
    {
        if(cents<=0)return "";
        var whole=cents%100==0;var number=whole?(cents/100).ToString(CultureInfo.InvariantCulture):(cents/100d).ToString("0.00",CultureInfo.InvariantCulture);
        return (currency??"").ToUpperInvariant() switch
        {
            "USD"=>"$"+number,"EUR"=>number.Replace('.',',')+"€","GBP"=>"£"+number,"RUB"=>number.Replace('.',',')+" руб.","KZT"=>number+" ₸","UAH"=>number.Replace('.',',')+"₴",
            ""=>number,var other=>number+" "+other
        };
    }
}

public static class GameRelease
{
    static readonly string[] Months=["янв","фев","мар","апр","мая","июн","июл","авг","сен","окт","ноя","дек"];
    // "24 фев. 2022 г." is a date; "2027", "Ещё не объявлена" and "Скоро выйдет" are not released yet.
    public static (int Year,bool Soon) Parse(string? text,DateTime today)
    {
        text=(text??"").Trim();
        if(text.Length==0)return (0,false);
        var date=Regex.Match(text,@"^(\d{1,2})\s+(\p{L}{3})\p{L}*\.?\s+(\d{4})");
        if(date.Success&&int.TryParse(date.Groups[1].Value,out var day)&&int.TryParse(date.Groups[3].Value,out var year))
        {
            var month=Array.IndexOf(Months,date.Groups[2].Value.ToLowerInvariant())+1;
            if(month>0&&day is >=1 and <=31&&year is >1970 and <2200)
            {
                try{return (year,new DateTime(year,month,Math.Min(day,DateTime.DaysInMonth(year,month)))>today.Date);}catch(ArgumentOutOfRangeException){}
            }
            return (year,false);
        }
        var any=Regex.Match(text,@"\b(19|20)\d{2}\b");
        if(any.Success&&int.TryParse(any.Value,out var rough))return (rough,rough>=today.Year);
        return (0,true);
    }
}

// Steam's own genre names, picked from the tags a search row carries; the genre chips on the games page use the same tags.
public static class GameGenres
{
    public sealed record Genre(int Tag,string Key,string Name,string Chip);
    public static readonly Genre[] All=
    [
        new(19,"action","Экшены","Экшены"),new(122,"rpg","Ролевые игры","Ролевые"),new(9,"strategy","Стратегии","Стратегии"),new(21,"adventure","Приключенческие игры","Приключения"),
        new(599,"simulation","Симуляторы","Симуляторы"),new(1774,"shooter","Шутеры","Шутеры"),new(1667,"horror","Хорроры","Хорроры"),new(699,"racing","Гонки","Гонки"),
        new(701,"sports","Спортивные игры","Спорт"),new(1685,"coop","Кооператив","Кооператив"),new(1695,"openworld","Открытый мир","Открытый мир"),new(1662,"survival","Выживание","Выживание"),
        new(1664,"puzzle","Головоломки","Головоломки"),new(1716,"roguelike","Рогалики","Рогалики"),new(492,"indie","Инди","Инди")
    ];
    static readonly int[] CardTags=[19,122,9,21,599,699,701,492];
    public static Genre? ByKey(string? key)=>All.FirstOrDefault(x=>x.Key==key);
    public static string Names(IEnumerable<int> tags)
    {
        var genres=tags.Where(CardTags.Contains).Distinct().Take(2).Select(tag=>All.First(x=>x.Tag==tag).Name).ToArray();
        return genres.Length==0?"":string.Join(", ",genres);
    }
}

// One list of games: a title search, or a sort (popular, specials, new, soon) with an optional genre, page by page.
public sealed record GameBrowse(string Query="",string Sort="popular",string Genre="",int Page=1)
{
    public const string Popular="popular",Specials="specials",New="new",Soon="soon";
    public static readonly string[] Sorts=[Popular,Specials,New,Soon];
    public static string SortName(string sort)=>sort switch{Specials=>"Скидки",New=>"Новинки",Soon=>"Скоро",_=>"Популярные"};
    public string Title=>Query.Length>0?"Результаты поиска":(GameGenres.ByKey(Genre) is {} genre?genre.Name+" · ":"")+Sort switch{Specials=>"Игры со скидкой",New=>"Новинки",Soon=>"Скоро выйдут",_=>"Популярные игры"};
    public string Subtitle=>Query.Length>0?"Результаты для «"+Query+"».":Sort switch
    {
        Specials=>"Самые покупаемые игры Steam, которые сейчас продаются со скидкой.",
        New=>"Свежие релизы Steam, которые уже набирают популярность.",
        Soon=>"Самые ожидаемые игры: их чаще всего добавляют в список желаемого.",
        _=>"Самые продаваемые игры Steam для Windows."
    };
}
public sealed record GameShelfSpec(string Key,string Title,string Sort,string Genre,int Rows);
public static class GameShelves
{
    // The home page: four main shelves, then one for the genres people look for most.
    public static readonly GameShelfSpec[] Home=
    [
        new("popular","Популярное",GameBrowse.Popular,"",2),new("specials","Со скидкой",GameBrowse.Specials,"",1),new("new","Новинки",GameBrowse.New,"",2),new("soon","Скоро выйдут",GameBrowse.Soon,"",1),
        new("action","Экшены",GameBrowse.Popular,"action",1),new("rpg","Ролевые игры",GameBrowse.Popular,"rpg",1),new("strategy","Стратегии",GameBrowse.Popular,"strategy",1),new("coop","Играть вместе",GameBrowse.Popular,"coop",1)
    ];
}
