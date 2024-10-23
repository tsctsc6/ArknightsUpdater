namespace ArknightsUpdater;

public class PollingResult
{
    public bool IsNewVersion { get; set; } = false;
    public string FileUri { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
}
