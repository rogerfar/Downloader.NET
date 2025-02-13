namespace DownloaderNET;

internal class TickCounter : ITickCounter
{
    /// <summary>
    /// Retrieves the number of milliseconds that have elapsed since the system was started, up 
    /// to 24.9 days. After 24.9 days this will start again from 0.
    /// </summary>
    /// <returns>milliseconds since system start</returns>
    Int32 ITickCounter.GetTickCount()
    {
        return Environment.TickCount & Int32.MaxValue;
    }
}
