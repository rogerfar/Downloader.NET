namespace DownloaderNET;

public class Chunk
{
    public Int64 StartByte { get; set; }
    public Int64 EndByte { get; set; }
    public Int64 LengthBytes { get; set; }
    public Int64 DownloadBytes { get; set; }
    public Double Speed { get; set; }
    public Boolean Completed { get; set; }
    public Double Progress { get; set; }

    public Boolean IsActive => DownloadBytes > 0 && DownloadBytes < LengthBytes;
}
