namespace Kachalka.Updates;
public static class UpdateTrust
{
    public static string PublicKey
    {
        get
        {
            using var stream = typeof(UpdateTrust).Assembly.GetManifestResourceStream("Kachalka.UpdateCore.update-public.pem") ?? throw new InvalidOperationException("Missing update verification key");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
