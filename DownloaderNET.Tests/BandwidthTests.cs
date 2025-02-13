using Microsoft.AspNetCore.Mvc.Diagnostics;
using Xunit.Abstractions;

namespace DownloaderNET.Tests;

public class BandwidthTests
{


    private const int OncSec = 1000;

    [Fact]
    public void BandwidthSpeedLimit()
    {
        var mockCounter = new MockTickCounter();
        var limit = 10000;
        var b = new Bandwidth(limit, mockCounter);

        mockCounter.Counter += OncSec * 2;
        b.CalculateSpeed(limit * 2);
        Assert.Equal(limit, b.Speed);

    }

    [Fact]
    public void BandwidthSpeedHalfLimit()
    {
        var mockCounter = new MockTickCounter();
        var limit = 5000;
        var b = new Bandwidth(limit * 2, mockCounter);

        mockCounter.Counter += OncSec * 2;
        b.CalculateSpeed(limit * 2);
        Assert.Equal(limit, b.Speed);
    }

    [Fact]
    public void BandwidthPopSpeedRetrieveTime()
    {
        var mockCounter = new MockTickCounter();
        var limit = 10000;
        var b = new Bandwidth(limit, mockCounter);

        mockCounter.Counter += OncSec * 2;
        b.CalculateSpeed(limit * 3);
        Assert.Equal(1000, b.PopSpeedRetrieveTime());

    }
}