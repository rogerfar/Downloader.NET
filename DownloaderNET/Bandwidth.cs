namespace DownloaderNET;

internal class Bandwidth
{
    public Double Speed { get; private set; }

    private const Double OneSecond = 1000;
    private const Double Period = 500; // Speed is updated every Period milliseconds. Before first Period finishes, Speed is zero.

    private readonly Int64 _bandwidthLimit;

    private readonly ITickCounter _tickCounter;
    private Int32 _lastSecondCheckpoint;
    private Int64 _lastTransferredBytesCount;
    private Int32 _speedRetrieveTime;

    public Bandwidth(Int32 bandwidthLimit, ITickCounter tickCounter)
    {
        _bandwidthLimit = bandwidthLimit > 0 ? bandwidthLimit : Int64.MaxValue;
        _tickCounter = tickCounter;

        SecondCheckpoint();
    }
    public Bandwidth(Int32 bandwidthLimit) : this(bandwidthLimit, new TickCounter()) { }

    public void CalculateSpeed(Int64 receivedBytesCount)
    {
        var elapsedTime = Math.Max(_tickCounter.GetTickCount() - _lastSecondCheckpoint, 1);
        receivedBytesCount = Interlocked.Add(ref _lastTransferredBytesCount, receivedBytesCount);
        var momentSpeed = receivedBytesCount * OneSecond / elapsedTime;

        if (Period < elapsedTime)
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
        Interlocked.Exchange(ref _lastSecondCheckpoint, _tickCounter.GetTickCount());
        Interlocked.Exchange(ref _lastTransferredBytesCount, 0);
    }
}
