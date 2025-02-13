class TickCounter : ITickCounter
{
    public TickCounter()
    {
    }



    /// <summary>
    /// Retrieves the number of milliseconds that have elapsed since the system was started, up 
    /// to 24.9 days. After 24.9 days this will start again from 0.
    /// </summary>
    /// <returns>milliseconds since system start</returns>
    int ITickCounter.GetTickCount()
    {
        return Environment.TickCount & int.MaxValue;
    }
}