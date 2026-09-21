using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Corvids.Services;

namespace Corvids.Tests;

public class ProcessToolsTests
{
    [Fact]
    public void DescribeProcess_includes_name_and_pid_for_the_current_process()
    {
        using var self = Process.GetCurrentProcess();

        var text = ProcessTools.DescribeProcess(self.Id);

        Assert.Contains(self.ProcessName, text);
        Assert.Contains(self.Id.ToString(), text);
    }

    [Fact]
    public void DescribeProcess_falls_back_to_pid_for_an_unknown_process()
    {
        // A pid that is essentially never a live process.
        Assert.Contains("999999", ProcessTools.DescribeProcess(999999));
    }

    [Fact]
    public async Task FindListenersAsync_returns_empty_for_a_free_port()
    {
        var port = FreePort();

        Assert.Empty(await ProcessTools.FindListenersAsync(port));
        Assert.False(await ProcessTools.IsPortListeningAsync(port));
    }

    [Fact]
    public async Task IsPortListeningAsync_is_true_while_a_socket_listens()
    {
        // netstat is always present on Windows; on other platforms lsof may be missing, so scope this there.
        if (!OperatingSystem.IsWindows()) return;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(await ProcessTools.IsPortListeningAsync(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void FindNodeProcesses_never_returns_null()
    {
        Assert.NotNull(ProcessTools.FindNodeProcesses());
    }

    [Fact]
    public async Task ListeningPortsAsync_reports_a_bound_socket_with_its_owning_pid()
    {
        if (!OperatingSystem.IsWindows()) return; // netstat -o gives pids on Windows; lsof may be absent elsewhere

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var all = await ProcessTools.ListeningPortsAsync();
            var mine = Process.GetCurrentProcess().Id;
            Assert.Contains(all, x => x.Port == port && x.Pid == mine);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
