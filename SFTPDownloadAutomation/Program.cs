using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Serilog;
using SFTPDownloadAutomation;
using SFTPDownloadAutomation.Models;
using SFTPDownloadAutomation.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CKYC SFTP Download Automation";
});

// Load configuration
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile(
        "appsettings.json",
        optional: false,
        reloadOnChange: true);

// Read logging configuration
var loggingSettings =
    builder.Configuration
        .GetSection("Logging")
        .Get<LoggingSettings>()
    ?? throw new InvalidOperationException(
        "Logging configuration is missing.");

// Create log directory
string? logDirectory =
    Path.GetDirectoryName(loggingSettings.LogFilePath);

if (!string.IsNullOrWhiteSpace(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        loggingSettings.LogFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true)
    .CreateLogger();

// Use Serilog
builder.Services.AddSerilog();

// Bind SFTP configuration
builder.Services.Configure<SFTPSettings>(
    builder.Configuration.GetSection("SFTP"));

builder.Services.Configure<ApiSettings>(
    builder.Configuration.GetSection("ApiSettings"));

builder.Services.Configure<SchedulerSettings>(
    builder.Configuration.GetSection("Scheduler"));

builder.Services.Configure<FileProcessingSettings>(
    builder.Configuration.GetSection("FileProcessing"));

// Register services
builder.Services.AddSingleton<SFTPService>();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<FileProcessingService>();
builder.Services.AddHttpClient<ApiService>(
    (serviceProvider, httpClient) =>
    {
        ApiSettings apiSettings =
            serviceProvider
                .GetRequiredService<IOptions<ApiSettings>>()
                .Value;

        if (Uri.TryCreate(
            apiSettings.BaseUrl,
            UriKind.Absolute,
            out Uri? baseUri))
        {
            httpClient.BaseAddress =
                baseUri;
        }

        httpClient.Timeout =
            TimeSpan.FromSeconds(
                Math.Max(
                    1,
                    apiSettings.TimeoutSeconds));
    });
builder.Services.AddHostedService<Worker>();

using IHost host = builder.Build();

try
{
    Log.Information("==============================================");
    Log.Information("SFTPDownloadAutomation SERVICE STARTED");
    Log.Information("==============================================");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(
        ex,
        "Fatal error occurred in SFTPDownloadAutomation.");
}
finally
{
    Log.Information("==============================================");
    Log.Information("SFTPDownloadAutomation SERVICE STOPPED");
    Log.Information("==============================================");

    Log.CloseAndFlush();
}
