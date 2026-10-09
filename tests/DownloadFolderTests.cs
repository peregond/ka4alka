using Kachalka;
static class DownloadFolderTests
{
    public static void Run()
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        var root=Path.Combine(Path.GetTempPath(),"Kachalka-folders-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var parent=Path.Combine(root,"Выбранная папка");Directory.CreateDirectory(parent);
            var folder=DownloadFolders.Prepare(parent);
            Check(folder==Path.Combine(parent,"Ka4alka")&&Directory.Exists(folder),"choosing a parent creates the real Ka4alka download directory");
            var existing=Path.Combine(folder,"existing-file.txt");File.WriteAllText(existing,"keep");
            Check(DownloadFolders.Prepare(folder+Path.DirectorySeparatorChar)==folder&&File.ReadAllText(existing)=="keep"&&!Directory.Exists(Path.Combine(folder,"Ka4alka")),"choosing Ka4alka again reuses it and preserves existing files without nesting");
            var missing=Path.Combine(root,"missing");
            try{DownloadFolders.Prepare(missing);throw new Exception("Missing parent accepted");}catch(DirectoryNotFoundException){}
            Check(!Directory.Exists(missing),"a disappeared selected parent is not silently recreated");
            try{DownloadFolders.Prepare("relative-folder");throw new Exception("Relative parent accepted");}catch(ArgumentException){Console.WriteLine("PASS: download folder requires a fully qualified parent path");}
            var blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);File.WriteAllText(Path.Combine(blocked,"Ka4alka"),"keep file");
            try{DownloadFolders.Prepare(blocked);throw new Exception("File conflict accepted");}catch(IOException){}
            Check(File.ReadAllText(Path.Combine(blocked,"Ka4alka"))=="keep file","a conflicting Ka4alka file is preserved and folder selection fails safely");
        }
        finally{Directory.Delete(root,true);}
    }
}
