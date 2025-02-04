namespace DownloaderNET.Tests;

public class DownloaderTests : BaseTest
{
    [Fact]
    public async Task SingleChunk()
    {
        var sha = await Download(new Options
        {
            Parallel = 1
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
    public async Task MultipleChunks(Int32 parallel)
    {
        var sha = await Download(new Options
        {
            Parallel = parallel
        });
        Assert.Equal(GetHash("test.bin"), sha);
    }

    [Fact]
    public async Task ChunkOverBuffer()
    {
        var sha = await Download(new Options
        {
            Parallel = 81
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Large(Int32 parallel)
    {
        var sha = await Download(new Options
        {
            Parallel = parallel,
            ServerSize = 1000000
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task FailedChunk(Int32 parallel)
    {
        await Assert.ThrowsAsync<HttpIOException>(() => Download(new Options
        {
            Parallel = parallel,
            ServerFailAfterBytes = 318893
        }));
    }
    
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task FailedRetryChunk(Int32 parallel)
    {
        await Assert.ThrowsAsync<HttpIOException>(() => Download(new Options
        {
            Parallel = parallel,
            ServerFailAfterBytes = 318893,
            DownloaderRetryCount = 1
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SuccessAfterRetryChunk(Int32 parallel)
    {
        var sha = await Download(new Options
        {
            Parallel = parallel,
            ServerFailAfterBytes = 20000,
            DownloaderRetryCount = 2
        });
        var shab = GetHash("test.bin");
        Assert.Equal(shab, sha);
    }

    [Fact]
    public async Task RequestTimeout()
    {
        await Assert.ThrowsAsync<TaskCanceledException>(() => Download(new Options
        {
            DownloaderTimeout = 100,
            ServerRequestTimeout = 31000
        }));
    }
    
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task StreamTimeout(Int32 parallel)
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Download(new Options
        {
            Parallel = parallel,
            DownloaderTimeout = 100,
            ServerStreamTimeout = 200
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task DownloadCancelled(Int32 parallel)
    {
        var cancellationToken = new CancellationTokenSource(2000);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Download(new Options
        {
            Parallel = parallel,
            DownloaderTimeout = 5000,
            ServerStreamTimeout = 2000
        }, cancellationToken.Token));
    }
}