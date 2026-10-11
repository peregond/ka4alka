# Temporary probe: which sources answer a title search, by English and Russian names.
import json, re, urllib.parse, urllib.request
UA={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36"}
def get(url):
    try:
        with urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=25) as r:
            return r.status, r.read().decode("utf-8","replace")
    except Exception as e:
        return str(e), ""
def zona(q):
    status,html=get("https://w6.zona.plus/search-form?query="+urllib.parse.quote(q))
    titles=re.findall(r'class="results-item-title"[^>]*>([^<]+)<',html)
    links=re.findall(r'href="(/(?:tvseries|movies)/[^"?#]+)"',html)
    return status,titles,links
for q in ["Солнечные противоположности","Breaking Bad","Во все тяжкие","Rick and Morty","Рик и Морти"]:
    status,titles,links=zona(q)
    print("zona",repr(q),status,len(titles),titles[:12])
    print("   ",[l for l in links if "protiv" in l or "solar" in l or "breaking" in l or "rick" in l or "tyazh" in l][:6])
for slug in ["solnechnye-protivopolozhnosti","solar-opposites"]:
    status,html=get("https://w6.zona.plus/tvseries/"+slug)
    print("zona page",slug,status,re.findall(r"<title>([^<]+)</title>",html)[:1])
status,body=get("https://v3.sg.media-imdb.com/suggestion/x/"+urllib.parse.quote("solar opposites")+".json")
try: print("imdb",status,[(d.get("l"),d.get("q"),d.get("y"),d.get("id")) for d in json.loads(body).get("d",[])][:5])
except Exception as e: print("imdb",status,body[:200])
status,body=get("https://v3.sg.media-imdb.com/suggestion/x/"+urllib.parse.quote("солнечные противоположности")+".json")
try: print("imdb ru",status,[(d.get("l"),d.get("q"),d.get("y")) for d in json.loads(body).get("d",[])][:5])
except Exception as e: print("imdb ru",status,body[:200])
