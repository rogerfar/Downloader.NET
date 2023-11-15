namespace DownloaderNET;

public class LogMessage
{
    public String Message { get; set; } = default!;
    public Int32 Thread { get; set; }
    public Exception? Exception { get; set; }
}
