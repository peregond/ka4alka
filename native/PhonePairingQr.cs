using QRCoder;

namespace Kachalka;

// The fragment stays in the browser; it is never sent in a URL to the HTTP server.
public static class PhonePairingQr
{
    public static string Link(string address,string code)=>address+"#pair="+Uri.EscapeDataString(code);
    public static byte[] Png(string link)=>PngByteQRCodeHelper.GetQRCode(link,QRCodeGenerator.ECCLevel.M,6);
}
