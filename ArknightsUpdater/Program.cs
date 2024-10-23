using Microsoft.Extensions.Configuration;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArknightsUpdater;

internal class Program
{
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
                    await downloader.DownloadAsync(res.FileUri, Path.Combine(DownloadPath, res.OriginalFileName));
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
}
