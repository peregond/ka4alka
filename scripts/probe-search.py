# Temporary probe: which sources answer a title search, by English and Russian names.
import json, re, urllib.parse, urllib.request
UA={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36"}
def get(url):
    try:
        with urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=25) as r:
            return r.status, r.read().decode("utf-8","replace")
    except Exception as e:
        return str(e), ""
for q in ["solar opposites","Solar Opposites","Солнечные противоположности","противоположности"]:
    status,html=get("https://w6.zona.plus/search-form?query="+urllib.parse.quote(q))
    titles=re.findall(r'class="results-item-title"[^>]*>([^<]+)<',html) or re.findall(r'<a[^>]+href="/(?:tvseries|movies)/[^"]+"[^>]*title="([^"]+)"',html)
    print("zona",repr(q),status,len(html),titles[:8])
    if not titles:
        print("  links:",re.findall(r'href="(/(?:tvseries|movies)/[^"]+)"',html)[:8])
    for section in ["series","movies"]:
        status,body=get("https://chatgpt.site/api/catalog?section="+section+"&q="+urllib.parse.quote(q))
        try: items=[(i.get("title"),i.get("originalTitle")) for i in json.loads(body).get("items",[])][:6]
        except Exception: items=body[:200]
        print("  index",section,status,items)
