using Kachalka;

static class PosterBlurTests
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        foreach(var (width,height) in new[]{(1,1),(1,96),(96,1),(64,96),(96,64)})
        {
            var uniform=Enumerable.Repeat(new byte[]{30,50,70,110},width*height).SelectMany(x=>x).ToArray();
            var snapshot=(byte[])uniform.Clone();var result=PosterBlur.Apply(uniform,width,height);
            Check(result.SequenceEqual(snapshot)&&uniform.SequenceEqual(snapshot)&&!ReferenceEquals(result,uniform),"blur preserves uniform colors and premultiplied alpha without mutating the poster");
        }
        const int size=96;var pixels=new byte[size*size*4];
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {
            var index=(y*size+x)*4;var color=(byte)((x+y)%2*255);
            pixels[index]=pixels[index+1]=pixels[index+2]=color;pixels[index+3]=255;
        }
        var blurred=PosterBlur.Apply(pixels,size,size);
        Check(blurred.Where((_,index)=>index%4==3).All(x=>x==255),"opaque posters remain opaque across all edges");
        var center=Enumerable.Range(24,48).SelectMany(y=>Enumerable.Range(24,48).Select(x=>(int)blurred[(y*size+x)*4])).ToArray();
        Check(center.Max()-center.Min()<4&&center.Average()>120&&center.Average()<135,"strong thumbnail blur removes high-frequency poster detail");
        bool Rejected(Action action){try{action();return false;}catch(ArgumentException){return true;}}
        Check(Rejected(()=>PosterBlur.Apply(new byte[97*96*4],97,96))&&Rejected(()=>PosterBlur.Apply([],1,1)),"blur cannot allocate full-resolution image buffers");
    }
}
