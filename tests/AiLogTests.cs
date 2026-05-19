using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

// AiLog holds process-wide static state; each test restores Enabled+Sink.
public class AiLogTests
{
    private static (bool, System.Action<string>?) Save() => (AiLog.Enabled, AiLog.Sink);

    private static void Restore((bool, System.Action<string>?) s)
    {
        AiLog.Enabled = s.Item1;
        AiLog.Sink = s.Item2;
    }

    [Fact]
    public void Print_WhenDisabled_DoesNotRouteToSink()
    {
        var saved = Save();
        try
        {
            var seen = new List<string>();
            AiLog.Sink = seen.Add;
            AiLog.Enabled = false;

            AiLog.Print("hello");

            Assert.Empty(seen);
        }
        finally { Restore(saved); }
    }

    [Fact]
    public void Print_WhenEnabled_RoutesMessageToSink()
    {
        var saved = Save();
        try
        {
            var seen = new List<string>();
            AiLog.Sink = seen.Add;
            AiLog.Enabled = true;

            AiLog.Print("alpha");
            AiLog.Print("beta");

            Assert.Equal(new[] { "alpha", "beta" }, seen);
        }
        finally { Restore(saved); }
    }

    [Fact]
    public void Print_WhenEnabledButSinkNull_DoesNotThrow()
    {
        var saved = Save();
        try
        {
            AiLog.Sink = null;
            AiLog.Enabled = true;

            AiLog.Print("noop"); // must not throw
        }
        finally { Restore(saved); }
    }
}
