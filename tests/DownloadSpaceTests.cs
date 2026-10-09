using Kachalka;
using System.Runtime.InteropServices;

static class DownloadSpaceTests
{
    public static void Run(string root)
    {
        void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        var folder=Path.Combine(root,"space-budget");Directory.CreateDirectory(folder);
        DownloadItem Item(string name,long size,long existing=0)
        {
            var path=Path.Combine(folder,name);return new(){Folder=folder,Files=[new(name,path,path+".part",size,existing*100d/size)]};
        }
        var first=Item("first.bin",1024);var other=Item("second.bin",2048);var elsewhere=Item("other.bin",4096);elsewhere.Folder=Path.Combine(root,"elsewhere");
        var allocated=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase){[first.Files[0].FullPath]=512};
        long free=DownloadSpace.SafetyBytes+512+2048;
        var checker=new DownloadSpace(path=>new(path==folder?"volume-one":"volume-two",free),path=>allocated.GetValueOrDefault(path));
        var result=checker.Check(first,[other,elsewhere]);
        Check(result.HasEnoughSpace&&result.OwnRemainingBytes==512&&result.OtherRemainingBytes==2048&&result.RequiredBytes==free,"disk budget includes existing bytes, other active files on the same volume and one fixed reserve");
        free--;Check(!checker.Check(first,[other]).HasEnoughSpace,"one byte below the combined budget prevents a new transfer");
        allocated[first.Files[0].IncompletePath]=900;result=checker.Check(first,[]);
        Check(result.OwnRemainingBytes==124,"completed and partial paths for one payload are credited once");
        allocated[first.Files[0].FullPath]=1024;result=checker.Check(first,[]);
        Check(result.OwnRemainingBytes==0,"fully preallocated payload is not budgeted again from reported progress");
        first.Progress=100;free=0;Check(checker.Check(first,[other]).HasEnoughSpace,"verified complete files may seed even when the disk has no spare space");first.Progress=0;
        var duplicate=Item("first.bin",1024);result=checker.Check(first,[duplicate]);
        Check(result.OtherRemainingBytes==0,"shared payload paths are not reserved twice");
        duplicate.Files=[duplicate.Files[0] with{Size=4096}];result=checker.Check(first,[duplicate]);
        Check(result.OtherRemainingBytes==3072,"overlapping torrent paths budget the larger expected payload instead of silently skipping it");
        first.Progress=100;allocated.Clear();result=checker.Check(first,[]);
        Check(!result.HasEnoughSpace&&result.OwnRemainingBytes==1024&&result.RequiredBytes>1024,"cached completed progress cannot bypass the space guard after its file was deleted");first.Progress=0;
        var unavailable=new DownloadSpace(_=>throw new IOException("drive disconnected"),_=>0).Check(first,[]);
        Check(!unavailable.HasEnoughSpace&&unavailable.Error=="drive disconnected"&&unavailable.AvailableBytes==null,"unavailable destination is explicit and does not pretend the drive is full");
        var isolated=new DownloadSpace(path=>path==folder?new("healthy",long.MaxValue):throw new IOException("other drive disconnected"),_=>0).Check(first,[elsewhere]);
        Check(isolated.Error==null&&isolated.HasEnoughSpace,"failure to inspect another drive does not mark a healthy destination unavailable");
        var huge=Item("huge.bin",long.MaxValue);var hugeOther=Item("huge-other.bin",long.MaxValue);
        var overflow=new DownloadSpace(_=>new("same",long.MaxValue),_=>0).Check(huge,[hugeOther]);
        Check(overflow.RequiredBytes==long.MaxValue&&overflow.OwnRemainingBytes==long.MaxValue,"very large torrent budgets saturate without integer overflow");
        // This exercises the actual Windows volume/free-space API and disk
        // allocation accounting; no large file or reservation is created.
        var actualFile=Path.Combine(folder,"actual.bin");File.WriteAllBytes(actualFile,new byte[8192]);
        var actual=new DownloadItem{Folder=folder,Files=[new("actual.bin",actualFile,actualFile+".part",8192,0)]};
        var native=new DownloadSpace().Check(actual,[]);
        Check(native.Error==null&&native.AvailableBytes>0&&native.OwnRemainingBytes==0,"actual existing file allocation is credited using the destination volume");
        if(OperatingSystem.IsWindows())
        {
            var sparse=Path.Combine(folder,"actual-sparse.bin");
            using(var output=new FileStream(sparse,FileMode.Create,FileAccess.ReadWrite,FileShare.ReadWrite))
            {
                if(!DeviceIoControl(output.SafeFileHandle,0x000900c4,IntPtr.Zero,0,IntPtr.Zero,0,out _,IntPtr.Zero))
                {
                    var error=Marshal.GetLastWin32Error();
                    if(error is 1 or 50){Console.WriteLine("SKIP: fixture filesystem does not support Windows sparse files");return;}
                    throw new System.ComponentModel.Win32Exception(error);
                }
                output.SetLength(1024*1024);output.Write(new byte[4096]);output.Flush(true);
            }
            var sparseItem=new DownloadItem{Folder=folder,Files=[new("actual-sparse.bin",sparse,sparse+".part",1024*1024,99)]};
            var sparseBudget=new DownloadSpace().Check(sparseItem,[]);
            Check(sparseBudget.Error==null&&sparseBudget.OwnRemainingBytes>512*1024,"actual sparse file's large logical size and stale progress never stand in for allocated storage");
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle file,uint code,IntPtr input,int inputBytes,IntPtr output,int outputBytes,out int bytesReturned,IntPtr overlapped);
}
