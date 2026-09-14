using System.Diagnostics;
using System.IO;

namespace Corvids.Services;

/// <summary>Cross-platform helpers for finding who owns a TCP port and killing stray node processes.</summary>
public static class ProcessTools
{
    /// <summary>PIDs of processes listening on <paramref name="port"/>.</summary>
    public static async Task<List<int>> FindListenersAsync(int port)
    {
        var pids = new HashSet<int>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = await RunAsync("netstat", "-ano", "-p", "tcp");
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || parts[3] != "LISTENING") continue;
                    if (parts[1].EndsWith($":{port}") && int.TryParse(parts[4], out var pid)) pids.Add(pid);
                }

                var v6 = await RunAsync("netstat", "-ano", "-p", "tcpv6");
                foreach (var line in v6.Split('\n'))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || parts[3] != "LISTENING") continue;
                    if (parts[1].EndsWith($":{port}") && int.TryParse(parts[4], out var pid)) pids.Add(pid);
                }
            }
            else
            {
                var output = await RunAsync("lsof", "-nP", $"-iTCP:{port}", "-sTCP:LISTEN", "-t");
                foreach (var line in output.Split('\n'))
                {
                    if (int.TryParse(line.Trim(), out var pid)) pids.Add(pid);
                }
            }
        }
        catch
        {
            // netstat / lsof missing: report nothing rather than crash
        }

        return pids.ToList();
    }

    public static async Task<bool> IsPortListeningAsync(int port) => (await FindListenersAsync(port)).Count > 0;

    /// <summary>Waits until nothing listens on the port, up to the timeout. Returns true when free.</summary>
    public static async Task<bool> WaitForPortFreeAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.Now + timeout;
        while (DateTime.Now < deadline)
        {
            if (!await IsPortListeningAsync(port)) return true;
            await Task.Delay(200);
        }

        return !await IsPortListeningAsync(port);
    }

    public static string DescribeProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return $"{process.ProcessName} (PID {pid})";
        }
        catch
        {
            return $"PID {pid}";
        }
    }

    public static bool KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Every node process on the machine, whoever started it.</summary>
    public static List<Process> FindNodeProcesses() => Process.GetProcessesByName("node").ToList();

    public record NodeProcess(int Pid, string CommandLine, string? Cwd);

    /// <summary>
    /// Node processes that belong to <paramref name="directory"/>: their working directory is inside it, or their
    /// command line references it (e.g. a script path under its node_modules).
    /// </summary>
    public static async Task<List<NodeProcess>> FindNodeProcessesInAsync(string directory)
    {
        var all = await ListNodeCommandLinesAsync();
        var needle = NormalizePath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return all.Where(p =>
            (p.Cwd is { } cwd && NormalizePath(cwd) is var c &&
             (c.Equals(needle, comparison) || c.StartsWith(needle + "/", comparison))) ||
            NormalizePath(p.CommandLine).Contains(needle, comparison)).ToList();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static async Task<List<NodeProcess>> ListNodeCommandLinesAsync()
    {
        var result = new List<NodeProcess>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Process.StartInfo is not readable for other processes on Windows; CIM has the command lines.
                const string script = "Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | " +
                                      "ForEach-Object { \"$($_.ProcessId)`t$($_.CommandLine)\" }";
                var output = await RunAsync("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script);
                foreach (var line in output.Split('\n'))
                {
                    var tab = line.IndexOf('\t');
                    if (tab > 0 && int.TryParse(line[..tab], out var pid))
                        result.Add(new NodeProcess(pid, line[(tab + 1)..].Trim(), ProcessCwd.TryGet(pid)));
                }
            }
            else
            {
                var output = await RunAsync("ps", "-eo", "pid=,args=");
                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    var space = line.IndexOf(' ');
                    if (space <= 0 || !int.TryParse(line[..space], out var pid)) continue;

                    var args = line[(space + 1)..].Trim();
                    var exe = Path.GetFileName(args.Split(' ')[0]);
                    if (exe == "node" || exe.StartsWith("node", StringComparison.Ordinal))
                        result.Add(new NodeProcess(pid, args, ProcessCwd.TryGet(pid)));
                }
            }
        }
        catch
        {
            // powershell / ps unavailable: fall through with what we have
        }

        return result;
    }

    /// <summary>Kills every node process on the machine. Returns how many were killed.</summary>
    public static int KillAllNode()
    {
        var killed = 0;
        foreach (var process in FindNodeProcesses())
        {
            try
            {
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch
            {
                // already gone or access denied
            }
            finally
            {
                process.Dispose();
            }
        }

        return killed;
    }

    private static async Task<string> RunAsync(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
