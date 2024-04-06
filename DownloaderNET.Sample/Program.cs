using DownloaderNET;

const String url = "https://sea2.download.real-debrid.com/d/VJVN43U2ETMLW20/PAW.Patrol.The.Mighty.Movie.2023.1080p.AMZN.WEBRip.DDP5.1.x265.10bit-GalaxyRG265.mkv";
const String path = "result.mp4";
const String hash = "7b3d96bd611dd68a6d7e185dce41f46c8ec4b8e013dd27bb965931ffe917dfb2";

if (File.Exists(path))
{
    File.Delete(path);
}

var downloader = new Downloader(url, path, new Settings
{
    Parallel = 8,
    BufferSize = 4096,
    ChunkSize = 0,
    MaximumBytesPerSecond = 1024 * 1024 * 100,
    RetryCount = 5,
    Timeout = 30000,
    UpdateTime = 10
});

downloader.OnLog = (message, _) =>
{
    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] [{message.Thread}] {message.Message}");

    if (message.Exception != null)
    {
        Console.WriteLine(message.Exception.StackTrace);
    }
};

var last = -1;
downloader.OnProgress += (chunks, fileQueue) =>
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
        else if (chunk.IsActive)
        {
            Console.WriteLine($"Thread {i} @ {chunk.Speed.ToMemoryMensurableUnit()}/s ({chunk.Progress:N2}%)");
        }
    }

    Console.WriteLine($"Avg {chunks.Where(m => m.IsActive).Sum(m => m.Speed).ToMemoryMensurableUnit()}/s ({p}%)");
    Console.WriteLine($"FileQueue length: {fileQueue}");
};

var start = DateTimeOffset.UtcNow;

var complete = false;
downloader.OnComplete += async (contentSize, ex) =>
{
    if (ex != null)
    {
        Console.WriteLine(ex.Message);
    }

    var elapsed = DateTimeOffset.UtcNow - start;
    var speed = contentSize / elapsed.TotalSeconds;

    Console.WriteLine($"Downloaded {contentSize.ToMemoryMensurableUnit()} in {elapsed} (avg {speed.ToMemoryMensurableUnit()})");

    using var sha256 = System.Security.Cryptography.SHA256.Create();

    await using var fileStream = File.OpenRead(path);

    var fileHash = BitConverter.ToString(sha256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();

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