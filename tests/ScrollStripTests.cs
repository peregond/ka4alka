using Kachalka;

static class ScrollStripTests
{
    public static void Run()
    {
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS: "+reason);}
        // 27 seasons of "N сезон" with their counts are far wider than any window; three seasons are not.
        Check(StripLayout.Overflows(2400,900)&&!StripLayout.Overflows(300,900),"a long series scrolls sideways and a short one keeps a plain strip");
        Check(!StripLayout.Overflows(900,900)&&!StripLayout.Overflows(900.4,900)&&StripLayout.Overflows(901,900),"a strip that fits exactly, give or take rounding, does not scroll");
        Check(!StripLayout.Overflows(2400,0)&&!StripLayout.Overflows(2400,-40),"a strip that has not been laid out yet never claims to overflow");

        // The chosen season (a 90 px segment 2000 px in) has to be visible without hunting for it.
        Check(StripLayout.Reveal(2000,90,0,800,1600)==1600,"a chosen segment near the end is shown and the strip stops at its end");
        Check(StripLayout.Reveal(1000,90,0,800,1600)==645,"a chosen segment beyond the viewport is centred");
        Check(StripLayout.Reveal(100,90,0,800,1600)==0,"a chosen segment that is already visible does not move the strip");
        Check(StripLayout.Reveal(20,90,300,800,1600)==0,"a chosen segment before the viewport scrolls back, never below zero");
        Check(StripLayout.Reveal(700,90,300,800,1600)==300,"a segment fully inside a scrolled viewport keeps the offset");
        Check(StripLayout.Reveal(790,90,0,800,1600)==435,"a segment cut by the viewport edge is brought in whole");
        Check(StripLayout.Reveal(500,90,40,0,1600)==40&&StripLayout.Reveal(500,90,900,800,0)==0,"without a viewport or a scrollable range the offset is only clamped");

        // Arrow presses cover most of the viewport and stay inside the strip.
        Check(StripLayout.Page(0,800,1600,1)==640&&StripLayout.Page(640,800,1600,1)==1280&&StripLayout.Page(1280,800,1600,1)==1600,"the forward arrow pages through the strip and stops at its end");
        Check(StripLayout.Page(1600,800,1600,-1)==960&&StripLayout.Page(300,800,1600,-1)==0,"the back arrow pages back and stops at the start");
        Check(StripLayout.Page(10,800,1600,0)==10&&StripLayout.Page(0,800,0,1)==0,"no direction or nothing to scroll leaves the strip where it is");
    }
}
