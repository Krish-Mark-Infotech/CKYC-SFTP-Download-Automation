namespace SFTPDownloadAutomation.Models;

public class ApiSettings
{
    public int MaxParallelism { get; set; } = 10;

    public string BaseUrl { get; set; } = string.Empty;

    public string ProcessFileEndpoint { get; set; } = string.Empty;

    public string FileFormFieldName { get; set; } = "file";

    public int TimeoutSeconds { get; set; } = 100;
}
