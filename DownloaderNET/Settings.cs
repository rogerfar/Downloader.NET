namespace DownloaderNET;

public class Settings
{
    /**
     * The amount of parallel chunks to download the file with.
     * Default = 1
     */
    public Int32 ChunkCount { get; set; }

    /**
     * The buffer size to process files with. Higher is faster but uses more memory.
     * Default = 4096
     */
    public Int32 BufferSize { get; set; }

    /**
     * Maximum bytes per second to download with, per chunk. When set to zero, chunks are not throttled.
     * Default = 0
     */
    public Int32 MaximumBytesPerSecond { get; set; }

    /**
     * Amount of times it will retry to download a chunk. If it still fails after that, it will fail the complete download.
     * Default = 0
     */
    public Int32 RetryCount { get; set; }

    /**
     * Timeout in ms before a chunk download will time out when no data has been received.
     * Default = 30000
     */
    public Int32 Timeout { get; set; }

    /**
     * Time in ms when the onProgress event fires in ms. Lower numbers increase CPU consumption.
     * Default = 1000
     */
    public Int32 UpdateTime { get; set; }
}