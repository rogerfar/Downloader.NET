# Downloader.NET
A simple high performance downloader for .NET with multi-chunk and bandwidth throttling support.

```csharp
using Downloader;

const String url = "http://speedtest.newark.linode.com/100MB-newark.bin";
const String path = "result.mp4";
const String hash = "7b3d96bd611dd68a6d7e185dce41f46c8ec4b8e013dd27bb965931ffe917dfb2";

var downloader = new Downloader(url, path, new Settings
{
    ChunkCount = 8,
    BufferSize = 4096,
    MaximumBytesPerSecond = 0,
    RetryCount = 0,
    Timeout = 5000
});

downloader.OnProgress += chunks =>
{
    Console.WriteLine($"Thread {i} @ {chunk.Speed.ToMemoryMensurableUnit()}/s ({chunk.Progress:N2}%)");
};

downloader.OnComplete += async ex =>
{
    if (ex != null)
    {
        Console.WriteLine(ex.Message);
    }
	else
	{
		Console.WriteLine("Complete");
	}
};

await downloader.Download();

Console.WriteLine("Downloading");
Console.ReadKey();

```

The following settings can be passed to the constructor:

* `ChunkCount`: Download a file with multiple threads to the server. The file will be split in equal parts.
* `BufferSize`: The amount of data from the server to buffer and write to the file. Higher will be faster, but use more memory.
* `MaximumBytesPerSecond`: Limit a single chunk with a maximum bytes per second.
* `RetryCount`: Retry a chunk this many times before failing the download.
* `Timeout`: When no data is received this amount of milliseconds, the download will error (or retry if `RetryCount` has not been reached yet).
