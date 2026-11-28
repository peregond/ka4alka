using Kachalka;

static class FavoriteSyncTests
{
    public static void Run()
    {
        int checks=0;
        void Check(bool ok,string what){if(!ok)throw new Exception("favourite sync: "+what);checks++;Console.WriteLine("PASS: favourite sync "+what);}
        var marks=new Dictionary<string,FavoriteMark>();
        FavoriteSync.Reconcile(marks,["movies:a","series:b","games:x","bad id"],100);
        Check(marks.Count==2&&marks["movies:a"] is {Removed:false,At:100},"new favourites get a mark, other kinds are ignored");
        FavoriteSync.Reconcile(marks,["movies:a"],200);
        Check(marks["movies:a"].At==100&&marks["series:b"] is {Removed:true,At:200},"an unchanged favourite keeps its time; a vanished one is marked removed");
        FavoriteSync.Reconcile(marks,["movies:a","series:b"],300);
        Check(marks["series:b"] is {Removed:false,At:300},"a removed title added again is a newer addition");

        var (add,remove)=FavoriteSync.Merge(marks,[new("movies:c",250,false),new("movies:a",50,true),new("series:b",400,true),new("movies:a",100,false)]);
        Check(add.SequenceEqual(["movies:c"])&&remove.SequenceEqual(["series:b"]),"newer marks win, older ones are ignored");
        Check(marks["movies:a"] is {Removed:false,At:100},"an older removal does not undo a newer addition");
        var (again,gone)=FavoriteSync.Merge(marks,[new("movies:c",250,false),new("series:b",400,true)]);
        Check(again.Count==0&&gone.Count==0,"repeating a sync changes nothing");
        var (a2,r2)=FavoriteSync.Merge(marks,[new("evil:../../x",999,false),new("movies:ok",0,false),new("movies:fine",5,false)]);
        Check(a2.SequenceEqual(["movies:fine"])&&r2.Count==0,"malformed ids and zero times are rejected");

        // Two devices converge after exchanging marks in both directions.
        var laptop=new Dictionary<string,FavoriteMark>();var tv=new Dictionary<string,FavoriteMark>();
        FavoriteSync.Reconcile(laptop,["movies:one","movies:two"],1000);
        FavoriteSync.Reconcile(tv,["movies:two","movies:three"],1100);
        FavoriteSync.Merge(tv,laptop.Values);FavoriteSync.Merge(laptop,tv.Values);
        Check(laptop.Keys.OrderBy(x=>x).SequenceEqual(tv.Keys.OrderBy(x=>x))&&laptop.Count==3,"two devices end with the same set of marks");
        Console.WriteLine($"PASS: favourite sync ({checks} checks)");
    }
}
