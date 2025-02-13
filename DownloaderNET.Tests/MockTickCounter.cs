namespace DownloaderNET.Tests;

internal class MockTickCounter : ITickCounter
{
    public Int32 Counter = 0;

    Int32 ITickCounter.GetTickCount()
    {
        return Counter & Int32.MaxValue;
    }
}
