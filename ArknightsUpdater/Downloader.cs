using Serilog;
using System.Diagnostics;

namespace ArknightsUpdater;

public class Downloader
{
    HttpClient client;
    int bytesRead;
    int bytesRead2;

    public int BufferSize { get; set; } = 8 * 1024;
    public double RefreshDelayInSeconds { get; set; } = 0.5;
    public double MaxMegaBytesPerSecond { get; set; } = 1;

    public Downloader(HttpClient client)
    {
        this.client = client;
    }
    public async Task DownloadAsync(string uri, string filePath)
    {
        var resp = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var totalBytes = resp.Content.Headers.ContentLength;

        using Stream contentStream = await resp.Content.ReadAsStreamAsync();
        using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[BufferSize];
        long totalBytesRead = 0;

        double maxBytesPerSecond = MaxMegaBytesPerSecond * 1024 * 1024;
        var delayPerBufferInSeconds_d = BufferSize / maxBytesPerSecond;
        int refreshCount = (int)(RefreshDelayInSeconds / delayPerBufferInSeconds_d);
        var delayPerBufferInSeconds = TimeSpan.FromSeconds(delayPerBufferInSeconds_d * refreshCount);
        Stopwatch stopwatch = new();
        Log.Information("开始下载");
        while (true)
        {
            stopwatch.Restart();
            await Task.WhenAll(MoveAsync(contentStream, buffer, fileStream, refreshCount), Task.Delay(delayPerBufferInSeconds));
            stopwatch.Stop();
            totalBytesRead += bytesRead2;
            if (totalBytes.HasValue)
            {
                // 计算并显示进度百分比
                Console.Out.WriteAsync($"\r{(double)totalBytesRead / 1024 / 1024:F2} MB, {(double)totalBytesRead / totalBytes.Value * 100:F2}%, {bytesRead2 / stopwatch.Elapsed.TotalSeconds / 1024 / 1024:F2} MB/s ");
            }
            else
            {
                // 如果无法获取文件大小，只显示已下载字节数
                Console.Out.WriteAsync($"\r{(double)totalBytesRead / 1024 / 1024:F2} MB, {bytesRead2 / stopwatch.Elapsed.TotalSeconds / 1024 / 1024:F2} MB/s ");
            }
            if (bytesRead == 0) break;
        }
        Log.Information("下载完成");
    }
    async Task MoveAsync(Stream stream1, byte[] buffer, Stream stream2, int count)
    {
        bytesRead2 = 0;
        for (int i = 0; i < count; i++)
        {
            bytesRead = await stream1.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) return;
            await stream2.WriteAsync(buffer, 0, bytesRead);
            bytesRead2 += bytesRead;
        }
    }
}
