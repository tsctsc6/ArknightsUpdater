using Microsoft.Extensions.Configuration;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArknightsUpdater;

internal class Program
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SW_MINIMIZE = 6;

    private const string DownloadPath = @".\Downloads";
    private const string LogPath = @".\Logs";
    private const string ConfigPath = @".\config.json";
    static readonly JsonSerializerOptions options = new() { WriteIndented = true };
    static readonly HttpClient client = new(new HttpClientHandler()
    {
        AllowAutoRedirect = false
    });

    public static IConfigurationRoot ConfigurationRoot { get; private set; }

    static void Init()
    {
        if (!Directory.Exists(DownloadPath)) Directory.CreateDirectory(DownloadPath);
        if (!Directory.Exists(LogPath)) Directory.CreateDirectory(LogPath);
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(LogPath, ".log"), rollingInterval: RollingInterval.Month)
            .CreateLogger();
        ConfigurationRoot = new ConfigurationBuilder()
            .AddJsonFile(ConfigPath, false, true)
            .Build();
        Log.Information("初始化完成");
    }

    static async Task Main(string[] args)
    {
        Init();
        TimeSpan pollingInterval = TimeSpan.Parse(ConfigurationRoot["PollingInterval"] ?? "06:00:00");
        while (true)
        {
            try
            {
                var res = await PollingAsync();
                if (res.IsNewVersion)
                {
                    var downloader = new Downloader(client)
                    {
                        BufferSize = ConfigurationRoot.GetValue<int>("Download:BufferSize"),
                        MaxMegaBytesPerSecond = ConfigurationRoot.GetValue<double>("Download:MaxMegaBytesPerSecond"),
                        RefreshDelayInSeconds = ConfigurationRoot.GetValue<double>("Download:RefreshDelayInSeconds"),
                    };
                    var newFilePath = Path.Combine(DownloadPath, res.OriginalFileName);
                    await downloader.DownloadAsync(res.FileUri, newFilePath);
                    await InstallAsync(newFilePath);
                    string oldFilePath = Path.Combine(DownloadPath, ConfigurationRoot["lastest_filename"]);
                    if (File.Exists(oldFilePath)) File.Delete(oldFilePath);
                    var jsonNode = JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath));
                    jsonNode!["lastest_filename"] = res.OriginalFileName;
                    await File.WriteAllTextAsync(ConfigPath, jsonNode.ToJsonString(options));
                }
            }
            catch (Exception e)
            {
                Log.Fatal("发生了意料之外的异常", e);
            }
            await Task.Delay(pollingInterval);
        }
    }

    static async Task<PollingResult> PollingAsync()
    {
        var resp = await client.GetAsync("https://ak.hypergryph.com/downloads/android_lastest");
        if (resp.StatusCode != HttpStatusCode.Found)
        {
            Log.Error("无法找到最新的安装包", resp);
            return new();
        }
        var fileUri = resp.Headers.Location;
        if (fileUri is null)
        {
            Log.Error("无法找到最新的安装包", resp);
            return new();
        }
        var originalFileName = fileUri.Segments[^1];
        if (originalFileName == ConfigurationRoot["lastest_filename"])
        {
            Log.Information("当前版本已经最新");
            return new();
        }
        else
        {
            Log.Information("发现更新的版本");
            return new()
            {
                IsNewVersion = true,
                FileUri = fileUri.OriginalString,
                OriginalFileName = originalFileName
            };
        }
    }

    static async Task InstallAsync(string filePath)
    {
        Log.Information("安装...");
        Process emulatorProcess = new Process
        {
            StartInfo = new()
            {
                FileName = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
                Arguments = ""
            }
        };
        emulatorProcess.Start();

        emulatorProcess.WaitForInputIdle();

        IntPtr hWnd = 0;
        while (hWnd == 0)
        {
            hWnd = emulatorProcess.MainWindowHandle;
        }
        ShowWindow(hWnd, SW_MINIMIZE);

        Process adbProcess = new Process
        {
            StartInfo = new()
            {
                FileName = @"adb",
                Arguments = $"-s 127.0.0.1:5555 install \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        adbProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Log.Information(args.Data);
            }
        };
        adbProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Log.Error(args.Data);
            }
        };
        adbProcess.Start();
        await adbProcess.WaitForExitAsync();

        emulatorProcess.Kill();
    }
}
