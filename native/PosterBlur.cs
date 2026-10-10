namespace Kachalka;

// Three small box passes approximate a strong Gaussian blur. Work stays bounded
// to a 96 px thumbnail, rather than an effect rendered at the window's DPI.
public static class PosterBlur
{
    public const int MaximumSide=96;
    public static byte[] Apply(byte[] pixels,int width,int height)
    {
        if(width<1||height<1||width>MaximumSide||height>MaximumSide||pixels.Length!=width*height*4)
            throw new ArgumentException("A bounded, four-channel thumbnail is required.");
        var source=(byte[])pixels.Clone();var target=new byte[source.Length];
        const int radius=9;
        for(var pass=0;pass<3;pass++)
        {
            Sweep(source,target,width,height,radius,true);(source,target)=(target,source);
            Sweep(source,target,width,height,radius,false);(source,target)=(target,source);
        }
        return source;
    }
    static void Sweep(byte[] source,byte[] target,int width,int height,int radius,bool horizontal)
    {
        var lines=horizontal?height:width;var length=horizontal?width:height;var count=radius*2+1;
        for(var line=0;line<lines;line++)for(var channel=0;channel<4;channel++)
        {
            int At(int position)=>horizontal?(line*width+Math.Clamp(position,0,length-1))*4+channel:(Math.Clamp(position,0,length-1)*width+line)*4+channel;
            var sum=0;for(var position=-radius;position<=radius;position++)sum+=source[At(position)];
            for(var position=0;position<length;position++)
            {
                target[At(position)]=(byte)((sum+count/2)/count);
                sum+=source[At(position+radius+1)]-source[At(position-radius)];
            }
        }
    }
}
