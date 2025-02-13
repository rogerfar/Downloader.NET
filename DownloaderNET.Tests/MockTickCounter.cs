

class MockTickCounter : ITickCounter
{

    public int Counter = 0;

    int ITickCounter.GetTickCount()
    {
        return Counter & int.MaxValue;
    }
}