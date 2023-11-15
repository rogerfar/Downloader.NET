using DownloaderNET;

const String url = "http://speedtest.newark.linode.com/100MB-newark.bin";
const String path = "result.mp4";
const String hash = "7b3d96bd611dd68a6d7e185dce41f46c8ec4b8e013dd27bb965931ffe917dfb2";

if (File.Exists(path))
{
    File.Delete(path);
}

var downloader = new Downloader(url, path, new Settings
{
    ChunkCount = 8,
    BufferSize = 4096,
    MaximumBytesPerSecond = 1024 * 1024 * 30,
    RetryCount = 5,
    Timeout = 30000,
    UpdateTime = 10
});

downloader.OnLog = message =>
{
    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] [{message.Thread}] {message.Message}");

    if (message.Exception != null)
    {
        Console.WriteLine(message.Exception.StackTrace);
    }
};

var last = -1;
downloader.OnProgress += chunks =>
{
    var p = (Int32) Math.Round(chunks.Sum(m => m.Progress) / chunks.Count);

    if (p == last)
    {
        return;
    }

    last = p;

    for (var i = 0; i < chunks.Count; i++)
    {
        var chunk = chunks[i];

        if (chunk.Completed)
        {
            Console.WriteLine($"Thread {i} completed");
        }
        else
        {
            Console.WriteLine($"Thread {i} @ {chunk.Speed.ToMemoryMensurableUnit()}/s ({chunk.Progress:N2}%)");
        }
    }

    Console.WriteLine($"Avg {chunks.Sum(m => m.Speed).ToMemoryMensurableUnit()}/s ({p}%)");
};

var complete = false;
downloader.OnComplete += async ex =>
{
    if (ex != null)
    {
        Console.WriteLine(ex.Message);
    }

    using var SHA256 = System.Security.Cryptography.SHA256.Create();

    await using var fileStream = File.OpenRead(path);

    var fileHash = BitConverter.ToString(SHA256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();

    if (fileHash != hash)
    {
        Console.WriteLine("NO Match");
    }
    else
    {
        Console.WriteLine("Match");
    }

    if (File.Exists(path))
    {
        File.Delete(path);
    }

    complete = true;
};

await downloader.Download();

while (!complete)
{
    await Task.Delay(10000);
}
Console.WriteLine("Done");