namespace DownloaderNET;

internal class Bandwidth
{
    public Double Speed { get; private set; }

    private const Double OneSecond = 1000;

    private readonly Int64 _bandwidthLimit;

    private Int32 _lastSecondCheckpoint;
    private Int64 _lastTransferredBytesCount;
    private Int32 _speedRetrieveTime;

    public Bandwidth(Int32 bandwidthLimit)
    {
        _bandwidthLimit = bandwidthLimit > 0 ? bandwidthLimit : Int64.MaxValue;

        SecondCheckpoint();
    }

    public void CalculateSpeed(Int64 receivedBytesCount)
    {
        var elapsedTime = Environment.TickCount - _lastSecondCheckpoint + 1;
        receivedBytesCount = Interlocked.Add(ref _lastTransferredBytesCount, receivedBytesCount);
        var momentSpeed = receivedBytesCount * OneSecond / elapsedTime; 

        if (OneSecond < elapsedTime)
        {
            Speed = momentSpeed;
            SecondCheckpoint();
        }

        if (momentSpeed >= _bandwidthLimit)
        {
            var expectedTime = receivedBytesCount * OneSecond / _bandwidthLimit;
            Interlocked.Add(ref _speedRetrieveTime, (Int32)expectedTime - elapsedTime);
        }
    }

    public Int32 PopSpeedRetrieveTime()
    {
        return Interlocked.Exchange(ref _speedRetrieveTime, 0);
    }
    
    private void SecondCheckpoint()
    {
        Interlocked.Exchange(ref _lastSecondCheckpoint, Environment.TickCount);
        Interlocked.Exchange(ref _lastTransferredBytesCount, 0);
    }
}
