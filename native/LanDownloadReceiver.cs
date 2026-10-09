using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Kachalka.Lan;
using MonoTorrent;

namespace Kachalka;

public sealed class LanDownloadReceiver(DownloadService downloads,Preferences preferences,string inboxDirectory)
{
    public async Task<LanDownloadReceipt> ReceiveAsync(LanDownloadRequest request,CancellationToken cancellation=default)
    {
        LanDownloadReceipt Reject(string message)=>new(){RequestId=request.RequestId,Message=message};
        if(request.SenderId==Guid.Empty||request.RequestId==Guid.Empty||request.SenderFingerprint is not {Length:64} fingerprint||fingerprint.Any(c=>!Uri.IsHexDigit(c)))
            return Reject("Устройство не подтверждено. Повтори сопряжение.");
        if(!preferences.FolderConfigured||string.IsNullOrWhiteSpace(preferences.Folder)||!Path.IsPathFullyQualified(preferences.Folder))
            return Reject("На принимающем компьютере нужно выбрать папку загрузок в настройках.");
        if(!await Task.Run(()=>Directory.Exists(preferences.Folder),cancellation).WaitAsync(cancellation))
            return Reject("Папка загрузок на принимающем компьютере недоступна. Проверь подключение диска и настройки папки.");
        var sender=request.SenderId.ToString("N");var requestId=request.RequestId.ToString("N");
        var digest=await Task.Run(()=>LanDownloadPayload.Digest(request),cancellation);
        var prior=downloads.Items.FirstOrDefault(x=>x.RemoteSenderId==sender&&x.RemoteRequestId==requestId&&x.RemoteSenderFingerprint==fingerprint);
        if(prior!=null)return prior.RemoteContentDigest==digest?Accepted(prior,true):Reject("Этот запрос уже использован для другой раздачи.");
        string? staged=null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            string source;
            if(request.Magnet is {Length:>0} magnet&&request.TorrentBytes==null)
            {
                if(magnet.Length>32768||!magnet.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase))return Reject("Некорректная magnet-ссылка.");
                MagnetLink.Parse(magnet);source=magnet;
            }
            else if(request.Magnet==null&&request.TorrentBytes is {Length:>0 and <=10*1024*1024} bytes)
            {
                await Task.Run(()=>Torrent.Load(bytes),cancellation);
                await Task.Run(()=>Directory.CreateDirectory(inboxDirectory),cancellation);
                staged=Path.Combine(inboxDirectory,Guid.NewGuid().ToString("N")+".torrent");
                await File.WriteAllBytesAsync(staged,bytes,cancellation);source=staged;
            }
            else return Reject("Передай magnet-ссылку или .torrent-файл размером до 10 МБ.");
            var media=Media(request.Media);
            var release=request.Release is {} details?new SourceEntry(requestId,details.Title??request.Title??"Раздача",details.Source??"Устройство в сети",SafePage(details.PageUrl)??"",request.Magnet,media?.ImageUrl,
                long.TryParse(details.Size,NumberStyles.Integer,CultureInfo.InvariantCulture,out var size)&&size>0?size:null,details.Seeds):null;
            var added=await downloads.Add(source,preferences.Folder,media,media?.ImageUrl,release,sender,requestId,fingerprint,digest);
            if(media==null&&!string.IsNullOrWhiteSpace(request.Title)&&string.IsNullOrWhiteSpace(added.MediaTitle))
            {
                added.MediaTitle=request.Title;added.Refresh();downloads.Save();
            }
            DiagnosticLog.Write("lan-download-accepted",new{request.RequestId,DownloadId=added.Id,added.LowSpacePaused});
            return Accepted(added,false);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            DiagnosticLog.Write("lan-download-rejected",new{request.RequestId,Error=error.GetType().Name});
            return Reject(error is IOException?"Не удалось сохранить раздачу на принимающем компьютере. Проверь папку и свободное место.":error.Message);
        }
        finally{if(staged!=null)try{File.Delete(staged);}catch(IOException){}catch(UnauthorizedAccessException){}}

        LanDownloadReceipt Accepted(DownloadItem item,bool repeated)=>new(){RequestId=request.RequestId,Accepted=true,DownloadId=item.Id,
            Message=repeated?"Эта раздача уже добавлена в очередь.":item.LowSpacePaused?"Раздача добавлена, но приостановлена: недостаточно свободного места.":"Раздача добавлена в очередь на этом устройстве."};
    }

    static string? SafePage(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0?uri.AbsoluteUri:null;
    static string? SafeImage(string? value)
    {
        if(SafePage(value) is not {} page)return null;
        var host=new Uri(page).IdnHost;
        return host=="zonapic.com"||host.EndsWith(".zonapic.com",StringComparison.OrdinalIgnoreCase)||
            host is "image.tmdb.org" or "st.kp.yandex.net" or "avatars.mds.yandex.net"?page:null;
    }
    static MediaItem? Media(LanMedia? media)
    {
        if(media==null||string.IsNullOrWhiteSpace(media.Title)||media.Category is not ("Фильмы" or "Сериалы"))return null;
        int.TryParse(media.Id,out var id);int.TryParse(media.Year,out var year);
        var card=DownloadMetadata.Card(new(){MediaTitle=media.Title,MediaSection=media.Category,MediaPageUrl=SafePage(media.PageUrl),MediaYear=year});
        return new(id,media.Title,media.Category,string.Join(", ",media.Genres??[]),Math.Clamp(year,0,2100),media.Rating??"—","—","#526B69")
        {PageUrl=card?.PageUrl,ImageUrl=SafeImage(media.ImageUrl)};
    }
}

public static class LanDownloadPayload
{
    public static string Digest(LanDownloadRequest request)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{request.Magnet,request.TorrentBytes,request.Title,request.Media,request.Release})));
    public static async Task<LanDownloadRequest> CreateAsync(string source,MediaItem? media=null,SourceEntry? release=null,CancellationToken cancellation=default)
    {
        string? magnet=null;byte[]? bytes=null;
        if(source.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase)){MagnetLink.Parse(source);magnet=source;}
        else
        {
            if(!File.Exists(source)||new FileInfo(source).Length>10*1024*1024)throw new InvalidDataException("Выбери .torrent-файл размером до 10 МБ.");
            bytes=await File.ReadAllBytesAsync(source,cancellation);await Task.Run(()=>Torrent.Load(bytes),cancellation);
        }
        return new(){Magnet=magnet,TorrentBytes=bytes,Title=media?.Title??release?.Title,
            Media=media==null?null:new(){Id=media.Id.ToString(CultureInfo.InvariantCulture),Category=media.Section,Title=media.Title,PageUrl=media.PageUrl,ImageUrl=media.ImageUrl,Year=media.Year.ToString(CultureInfo.InvariantCulture),Rating=media.CardRating,Genres=media.Genre.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray()},
            Release=release==null?null:new(){Title=release.Title,Source=release.Source,PageUrl=release.PageUrl,Size=release.Size?.ToString(CultureInfo.InvariantCulture),Seeds=release.Seeds}};
    }
}
