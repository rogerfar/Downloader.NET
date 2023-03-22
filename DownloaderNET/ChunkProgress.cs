namespace DownloaderNET;

public class ChunkProgress
{
    public Int64 StartByte { get; set; }
    public Int64 EndByte { get; set; }
    public Int64 LengthBytes { get; set; }
    public Int64 DownloadBytes { get; set; }
    public Double Speed { get; set; }
    public Boolean Completed { get; set; }
    public Double Progress { get; set; }
}
