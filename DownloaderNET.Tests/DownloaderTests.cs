namespace DownloaderNET.Tests;

public class DownloaderTests : BaseTest
{
    [Fact]
    public async Task SingleChunk()
    {
        var sha = await Download(new Options
        {
            DownloaderChunkCount = 1
        });
        Assert.Equal(GetHash("test.bin"), sha);
    }
    
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public async Task MultipleChunks(Int32 chunkCount)
    {
        var sha = await Download(new Options
        {
            DownloaderChunkCount = chunkCount
        });
        Assert.Equal(GetHash("test.bin"), sha);
    }

    [Fact]
    public async Task ChunkOverBuffer()
    {
        var sha = await Download(new Options
        {
            DownloaderChunkCount = 81
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Large(Int32 chunkCount)
    {
        var sha = await Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            ServerSize = 1000000
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task FailedChunk(Int32 chunkCount)
    {
        await Assert.ThrowsAsync<IOException>(() => Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            ServerFailAfterBytes = 20000
        }));
    }
    
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task FailedRetryChunk(Int32 chunkCount)
    {
        await Assert.ThrowsAsync<IOException>(() => Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            ServerFailAfterBytes = 20000,
            DownloaderRetryCount = 1
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SuccessAfterRetryChunk(Int32 chunkCount)
    {
        var sha = await Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            ServerFailAfterBytes = 20000,
            DownloaderRetryCount = 2
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task RequestTimeout(Int32 chunkCount)
    {
        await Assert.ThrowsAsync<TaskCanceledException>(() => Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            DownloaderTimeout = 100,
            ServerRequestTimeout = 200
        }));
    }
    
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task StreamTimeout(Int32 chunkCount)
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            DownloaderTimeout = 100,
            ServerStreamTimeout = 200
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task DownloadCancelled(Int32 chunkCount)
    {
        var cancellationToken = new CancellationTokenSource(1000);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Download(new Options
        {
            DownloaderChunkCount = chunkCount,
            ServerStreamTimeout = 100
        }, cancellationToken.Token));
    }
}