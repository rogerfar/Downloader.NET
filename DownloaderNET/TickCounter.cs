class TickCounter : ITickCounter
{
    public TickCounter()
    {
    }

    int ITickCounter.GetTickCount()
    {
        return Environment.TickCount & int.MaxValue;
    }
}