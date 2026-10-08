using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Kachalka;

public record DownloadVolume(string Id,long AvailableBytes);
public record DownloadSpaceCheck(long? AvailableBytes,long RequiredBytes,long OwnRemainingBytes,long OtherRemainingBytes,string? Error=null)
{
    public bool HasEnoughSpace=>Error==null&&AvailableBytes.HasValue&&AvailableBytes.Value>=RequiredBytes;
    public string Message=>Error!=null?"Не удалось проверить свободное место: "+Error:
        $"Свободно {DownloadService.FormatBytes(AvailableBytes??0)}, нужно {DownloadService.FormatBytes(RequiredBytes)}"+
        (OtherRemainingBytes>0?$" (включая {DownloadService.FormatBytes(OtherRemainingBytes)} для других активных загрузок)":"")+
        ". Освободи место и нажми «Продолжить».";
}

// The reservation is a budget, never an allocation. Existing sparse files are
// credited for allocated bytes, not their logical length or reported progress.
public sealed class DownloadSpace
{
    public const long SafetyBytes=256L*1024*1024;
    readonly Func<string,DownloadVolume> volume;
    readonly Func<string,long> allocated;
    public DownloadSpace(Func<string,DownloadVolume>? volume=null,Func<string,long>? allocated=null)
    {this.volume=volume??ReadVolume;this.allocated=allocated??AllocatedBytes;}

    public DownloadSpaceCheck Check(DownloadItem item,IReadOnlyList<DownloadItem> commitments)
    {
        try
        {
            var target=volume(item.Folder);
            var paths=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
            var own=Remaining(item.Files,paths);long other=0;
            foreach(var next in commitments)
            {
                if(next.Id==item.Id||next.Completed||next.Files.Count==0)continue;
                try
                {
                    if(string.Equals(volume(next.Folder).Id,target.Id,StringComparison.OrdinalIgnoreCase))
                        other=Add(other,Remaining(next.Files,paths));
                }
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    // An unplugged unrelated drive must not make this healthy
                    // folder unavailable. If both paths have the same root,
                    // retain a conservative budget without accessing that drive.
                    if(string.Equals(Path.GetPathRoot(Path.GetFullPath(next.Folder)),Path.GetPathRoot(Path.GetFullPath(item.Folder)),StringComparison.OrdinalIgnoreCase))
                        foreach(var file in next.Files.Where(f=>f.Size>0))other=Add(other,Claim(paths,Path.GetFullPath(file.FullPath),file.Size));
                }
            }
            // Fully verified downloads can seed even on a nearly full disk.
            var required=item.Completed&&own==0?0:Add(Add(own,other),SafetyBytes);
            return new(target.AvailableBytes,required,own,other);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException)
        {return new(null,0,0,0,error.Message);}
    }
    long Remaining(IReadOnlyList<DownloadFile> files,Dictionary<string,long> paths)
    {
        long total=0;
        foreach(var file in files)
        {
            if(file.Size<=0)continue;
            var path=Path.GetFullPath(file.FullPath);
            // MonoTorrent uses either the completed or partial path. Count the
            // largest existing representation once; a rename must not double it.
            var existing=Math.Max(allocated(file.FullPath),string.Equals(file.FullPath,file.IncompletePath,StringComparison.OrdinalIgnoreCase)?0:allocated(file.IncompletePath));
            total=Add(total,Claim(paths,path,Math.Max(0,file.Size-Math.Clamp(existing,0,file.Size))));
        }
        return total;
    }
    static long Claim(Dictionary<string,long> paths,string path,long remaining)
    {
        var previous=paths.GetValueOrDefault(path);
        if(remaining<=previous)return 0;
        paths[path]=remaining;return remaining-previous;
    }
    static long Add(long a,long b)=>a>long.MaxValue-b?long.MaxValue:a+b;
    static DownloadVolume ReadVolume(string folder)
    {
        var full=Path.GetFullPath(folder);
        if(!OperatingSystem.IsWindows())
        {
            var root=DriveInfo.GetDrives().Where(d=>full.StartsWith(d.RootDirectory.FullName,StringComparison.Ordinal)).OrderByDescending(d=>d.RootDirectory.FullName.Length).First();
            return new(root.Name,root.AvailableFreeSpace);
        }
        if(!GetDiskFreeSpaceEx(full,out var available,out _,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
        var mount=new StringBuilder(1024);
        if(!GetVolumePathName(full,mount,mount.Capacity))throw new Win32Exception(Marshal.GetLastWin32Error());
        var id=mount.ToString();var name=new StringBuilder(1024);
        if(GetVolumeNameForVolumeMountPoint(id,name,name.Capacity))id=name.ToString();
        else if(id.Length>=2&&id[1]==':')
        {
            // A mapped drive and the corresponding UNC share share one budget.
            int capacity=name.Capacity;
            if(WNetGetConnection(id[..2],name,ref capacity)==0)id=name.ToString().TrimEnd('\\')+"\\";
        }
        return new(id,available>long.MaxValue?long.MaxValue:(long)available);
    }
    static long AllocatedBytes(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return 0;
        if(!OperatingSystem.IsWindows())return new FileInfo(path).Length;
        Marshal.SetLastPInvokeError(0);var low=GetCompressedFileSize(path,out var high);
        if(low==uint.MaxValue&&Marshal.GetLastPInvokeError()!=0)throw new Win32Exception(Marshal.GetLastPInvokeError());
        var bytes=((ulong)high<<32)|low;return bytes>long.MaxValue?long.MaxValue:(long)bytes;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetDiskFreeSpaceEx(string path,out ulong available,out ulong total,out ulong free);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumePathName(string path,StringBuilder volume,int length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumeNameForVolumeMountPoint(string path,StringBuilder volume,int length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetCompressedFileSize(string path,out uint high);
    [DllImport("mpr.dll",CharSet=CharSet.Unicode)] static extern int WNetGetConnection(string local,StringBuilder remote,ref int length);
}
