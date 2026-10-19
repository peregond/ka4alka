using System.Security.Cryptography;
using System.Text.Json;
using Kachalka.MacUpdates;
using Kachalka.Updates;
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string HashFile(string file){using var input=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();}
if(args.Length==3&&args[0]=="--prepare-mac")
{
    var directory=Path.GetFullPath(args[1]);var version=args[2];var packages=new Dictionary<string,MacPackage>();
    foreach(var architecture in new[]{"arm64","x64"})
    {
        var name="Kachalka-macos-"+architecture+".zip";var archive=Path.Combine(directory,name);
        var catalog=JsonSerializer.SerializeToUtf8Bytes(MacComponents.Create(archive,version),json);
        var indexName="macos-components-"+architecture+".json";File.WriteAllBytes(Path.Combine(directory,indexName),catalog);
        var package=new MacPackage($"https://github.com/peregond/ka4alka/releases/download/v{version}/{name}",name,new FileInfo(archive).Length,HashFile(archive),new($"https://github.com/peregond/ka4alka/releases/download/v{version}/{indexName}",catalog.Length,Hash(catalog)));
        MacComponents.Verify(catalog,package,version);packages.Add(architecture,package);
    }
    var manifest=new MacManifest(1,"kachalka","stable",version,"macos","14.0",packages,"https://github.com/peregond/ka4alka/releases/tag/v"+version);manifest.Validate();
    var data=JsonSerializer.SerializeToUtf8Bytes(manifest,json);
    // The signing key stays in process memory and the CI secret store.
    using var rsa=RSA.Create();rsa.ImportFromPem(Environment.GetEnvironmentVariable("KACHALKA_UPDATE_SIGNING_KEY_PEM")??throw new InvalidOperationException("Missing release signing secret."));
    var signature=rsa.SignData(data,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    MacManifest.Verify(data,signature,UpdateTrust.PublicKey);
    File.WriteAllBytes(Path.Combine(directory,"macos-latest.json"),data);File.WriteAllBytes(Path.Combine(directory,"macos-latest.sig"),signature);
    foreach(var path in Directory.GetFiles(directory,"Kachalka-macos-*"))Console.WriteLine(Path.GetFileName(path)+" "+HashFile(path));
}
else if(args.Length==2&&args[0]=="--verify-mac")
{
    var directory=Path.GetFullPath(args[1]);
    var manifest=MacManifest.Verify(File.ReadAllBytes(Path.Combine(directory,"macos-latest.json")),File.ReadAllBytes(Path.Combine(directory,"macos-latest.sig")),UpdateTrust.PublicKey);
    foreach(var pair in manifest.Packages)
    {
        var path=Path.Combine(directory,pair.Value.FileName);
        if(new FileInfo(path).Length!=pair.Value.Size||HashFile(path)!=pair.Value.Sha256)throw new InvalidDataException("macOS archive differs from signed package.");
        MacComponents.Verify(File.ReadAllBytes(Path.Combine(directory,"macos-components-"+pair.Key+".json")),pair.Value,manifest.Version);
        var generated=MacComponents.Create(path,manifest.Version);
        var signed=MacComponents.Verify(File.ReadAllBytes(Path.Combine(directory,"macos-components-"+pair.Key+".json")),pair.Value,manifest.Version);
        if(!generated.Files.SequenceEqual(signed.Files))throw new InvalidDataException("macOS archive differs from signed component index.");
        Console.WriteLine("Verified macOS "+pair.Key+" "+manifest.Version);
    }
}
else throw new ArgumentException("Use --prepare-mac DIRECTORY VERSION or --verify-mac DIRECTORY.");
