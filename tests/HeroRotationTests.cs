using Kachalka;

static class HeroRotationTests
{
    public static void Run()
    {
        void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        Check(HeroRotation.Interval==TimeSpan.FromSeconds(8),"the banner changes its slide every eight seconds, as in the macOS version");
        Check(HeroRotation.CanAdvance(5,true,true,false,false,false),"a visible banner in an active window with nobody at it advances");
        Check(!HeroRotation.CanAdvance(1,true,true,false,false,false),"a single slide never advances");
        Check(!HeroRotation.CanAdvance(5,false,true,false,false,false),"a banner scrolled out of view does not advance");
        Check(!HeroRotation.CanAdvance(5,true,false,false,false,false),"an inactive window does not advance the banner");
        Check(!HeroRotation.CanAdvance(5,true,true,true,false,false),"the pointer or the keyboard focus on the banner stops it");
        Check(!HeroRotation.CanAdvance(5,true,true,false,true,false),"the pause button stops the banner");
        Check(!HeroRotation.CanAdvance(5,true,true,false,false,true),"reduced motion stops the banner");
        string[] ids=["a","b","c"];
        Check(HeroRotation.Next(ids,"a",1)=="b"&&HeroRotation.Next(ids,"c",1)=="a","the next slide wraps from the last to the first");
        Check(HeroRotation.Next(ids,"a",-1)=="c"&&HeroRotation.Next(ids,"b",-1)=="a","the previous slide wraps from the first to the last");
        Check(HeroRotation.Next(ids,"gone",1)=="b"&&HeroRotation.Next(ids,null,0)=="a","a selection that is gone falls back to the first slide");
        Check(HeroRotation.Next([],"a",1)==null,"no slides, no next slide");
    }
}
