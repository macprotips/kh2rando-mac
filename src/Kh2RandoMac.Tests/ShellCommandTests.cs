using System.Diagnostics;
using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class ShellCommandTests
{
    [Fact]
    public void Run_CapturesOutputAndSuccess()
    {
        var result = ShellCommand.Run("/bin/echo", "hello", "world");
        Assert.True(result.Succeeded);
        Assert.Equal("hello world", result.Output.Trim());
    }

    [Fact]
    public void Run_ReportsAFailingExitCodeWithoutThrowing()
    {
        var result = ShellCommand.Run("/usr/bin/false");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Run_CapturesStandardErrorSeparately()
    {
        // Some CrossOver builds print a command's answer on stderr, so callers need it.
        var result = ShellCommand.Run("/bin/sh", "-c", "echo out; echo err >&2");
        Assert.Equal("out", result.Output.Trim());
        Assert.Equal("err", result.Error.Trim());
    }

    [Fact]
    public void Run_ReturnsEmptyForAToolThatIsNotThere()
    {
        var result = ShellCommand.Run("/usr/bin/definitely-not-a-real-tool");
        Assert.False(result.Succeeded);
        Assert.Equal("", result.Output);
    }

    [Fact]
    public void Run_GivesUpOnAToolThatWillNotAnswer()
    {
        // The point of the timeout: a wedged tool must not freeze the window.
        var watch = Stopwatch.StartNew();
        var result = ShellCommand.Run("/bin/sleep", TimeSpan.FromMilliseconds(400), "30");
        watch.Stop();

        Assert.False(result.Succeeded);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10),
            $"timed out call took {watch.Elapsed}, so it did not give up");
    }

    [Fact]
    public void Run_SurvivesOutputLargerThanAPipeBuffer()
    {
        // Reading one pipe to completion before the other deadlocks here; this is the
        // shape that hung multi-minute installs before both were drained together.
        var result = ShellCommand.Run("/bin/sh", TimeSpan.FromSeconds(20), "-c",
            "for i in $(seq 1 2000); do echo 'out out out out out out out out'; " +
            "echo 'err err err err err err err err' >&2; done");
        Assert.True(result.Succeeded);
        Assert.True(result.Output.Length > 60000, $"stdout was {result.Output.Length} bytes");
        Assert.True(result.Error.Length > 60000, $"stderr was {result.Error.Length} bytes");
    }
}
