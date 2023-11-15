namespace DownloaderNET;

public class LogMessage
{
    public String Message { get; set; } = default!;
    public Int64 Thread { get; set; }
    public Exception? Exception { get; set; }
}
