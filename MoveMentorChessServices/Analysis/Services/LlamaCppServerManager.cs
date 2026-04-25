using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MoveMentorChessServices;

/// <summary>
/// Manages the lifecycle of a local llama-server process.
/// The server is started lazily on first use and stopped when the application exits.
/// Listens only on 127.0.0.1 — no network exposure.
/// </summary>
public sealed class LlamaCppServerManager : IDisposable
{
    private static readonly Lazy<LlamaCppServerManager> LazyInstance = new(() => new LlamaCppServerManager());

    private readonly object syncLock = new();
    private Process? serverProcess;
    private int resolvedPort;
    private bool disposed;

    private LlamaCppServerManager()
    {
    }

    public static LlamaCppServerManager Instance => LazyInstance.Value;

    public string? BaseUrl => resolvedPort > 0 ? $"http://127.0.0.1:{resolvedPort}" : null;

    public bool IsRunning
    {
        get
        {
            lock (syncLock)
            {
                return serverProcess is not null && !serverProcess.HasExited;
            }
        }
    }

    /// <summary>
    /// Ensures the server is running. If not already started, resolves configuration,
    /// picks a free port, starts the process, and waits for it to become healthy.
    /// </summary>
    public bool EnsureRunning(LlamaCppServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (syncLock)
        {
            if (disposed)
            {
                return false;
            }

            if (serverProcess is not null && !serverProcess.HasExited)
            {
                return true;
            }

            // Clean up any dead process reference.
            DisposeProcess();

            int port = config.Port > 0 ? config.Port : FindFreePort();
            if (port <= 0)
            {
                return false;
            }

            if (!TryStartServer(config, port))
            {
                return false;
            }

            resolvedPort = port;
            return WaitForHealthy(config.StartupTimeoutMs);
        }
    }

    public void Shutdown()
    {
        lock (syncLock)
        {
            KillServer();
            DisposeProcess();
            resolvedPort = 0;
        }
    }

    public void Dispose()
    {
        lock (syncLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            KillServer();
            DisposeProcess();
            resolvedPort = 0;
        }
    }

    private bool TryStartServer(LlamaCppServerConfig config, int port)
    {
        try
        {
            string workingDirectory = Path.GetDirectoryName(config.ServerPath) ?? AppContext.BaseDirectory;
            string modelArgument = LlamaCppAdviceModel.ResolveModelArgument(config.ModelPath, workingDirectory);

            ProcessStartInfo startInfo = new(config.ServerPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory
            };

            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(modelArgument);
            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add("127.0.0.1");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString());
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(Math.Max(512, config.ContextSize).ToString());
            startInfo.ArgumentList.Add("-ngl");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(config.GpuLayersArgument) ? LlamaGpuSettingsResolver.BalancedGpuLayersArgument : config.GpuLayersArgument);
            startInfo.ArgumentList.Add("--log-disable");

            Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }

            // Discard stdout/stderr asynchronously so buffers don't fill up.
            process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();

            serverProcess = process;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool WaitForHealthy(int timeoutMs)
    {
        string healthUrl = $"http://127.0.0.1:{resolvedPort}/health";
        Stopwatch stopwatch = Stopwatch.StartNew();

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (serverProcess is null || serverProcess.HasExited)
            {
                return false;
            }

            try
            {
                using HttpResponseMessage response = client.GetAsync(healthUrl).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    // llama-server returns {"status":"ok"} when the model is fully loaded.
                    // During loading it may return {"status":"loading model"} with 503.
                    // Double-check the body to confirm readiness.
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (body.Contains("ok", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Server not ready yet, keep polling.
            }

            Thread.Sleep(1000);
        }

        // Timed out — kill the server.
        KillServer();
        DisposeProcess();
        return false;
    }

    private void KillServer()
    {
        if (serverProcess is null)
        {
            return;
        }

        try
        {
            if (!serverProcess.HasExited)
            {
                serverProcess.Kill(entireProcessTree: true);
                serverProcess.WaitForExit(5000);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private void DisposeProcess()
    {
        serverProcess?.Dispose();
        serverProcess = null;
    }

    private static int FindFreePort()
    {
        try
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return -1;
        }
    }
}
