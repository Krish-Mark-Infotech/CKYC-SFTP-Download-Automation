namespace SFTPDownloadAutomation.Models;

public class SchedulerSettings
{
    public int IntervalMinutes { get; set; } = 5;

    public bool RunImmediately { get; set; } = true;
}
