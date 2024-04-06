namespace DownloaderNET;

public class Settings
{
    /**
     * The amount of tasks to download a chunk with.
     * Default = 1
     */
    public Int32 Parallel { get; set; }

    /**
     * The size in bytes of a chunk to download.
     * When unset (or 0), it automatically determines the optimal size based on the download file size.
     */
    public Int32 ChunkSize { get; set; }

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

    /**
     * Log level:
     * 0: Verbose
     * 1: Debug
     * 2: Information
     * 3: Warning
     * 4: Error
     * 5: Off
     */
    public Int32 LogLevel { get; set; }
}