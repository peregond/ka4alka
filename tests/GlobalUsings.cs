// WPF's SDK omits these two console defaults. The existing HTTP and file
// fixtures still need them when real bitmap decoding is tested in this host.
global using System.IO;
global using System.Net.Http;
