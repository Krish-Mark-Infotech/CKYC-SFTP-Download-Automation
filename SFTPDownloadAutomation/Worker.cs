using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFTPDownloadAutomation.Models;
using SFTPDownloadAutomation.Services;

namespace SFTPDownloadAutomation;

public class Worker : BackgroundService
{
    private readonly FileProcessingService _fileProcessingService;
    private readonly ILogger<Worker> _logger;
    private readonly SchedulerSettings _schedulerSettings;
    private readonly SemaphoreSlim _jobLock = new(1, 1);

    public Worker(
        FileProcessingService fileProcessingService,
        IOptions<SchedulerSettings> schedulerSettings,
        ILogger<Worker> logger)
    {
        _fileProcessingService = fileProcessingService;
        _schedulerSettings = schedulerSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        int intervalMinutes =
            Math.Max(
                1,
                _schedulerSettings.IntervalMinutes);

        _logger.LogInformation(
            "SFTP Download Automation Worker started. IntervalMinutes = {IntervalMinutes}, RunImmediately = {RunImmediately}",
            intervalMinutes,
            _schedulerSettings.RunImmediately);

        if (_schedulerSettings.RunImmediately)
        {
            await RunJobAsync(stoppingToken);
        }

        using var timer =
            new PeriodicTimer(
                TimeSpan.FromMinutes(intervalMinutes));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunJobAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "SFTP Download Automation Worker is stopping.");
        }
    }

    private async Task RunJobAsync(
        CancellationToken stoppingToken)
    {
        if (!await _jobLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogWarning(
                "Previous SFTP download automation job is still running. Skipping this schedule.");

            return;
        }

        try
        {
            _logger.LogInformation(
                "Scheduled SFTP download automation job started at {Time}.",
                DateTimeOffset.Now);

            await _fileProcessingService.RunAsync(stoppingToken);

            _logger.LogInformation(
                "Scheduled SFTP download automation job finished at {Time}.",
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Scheduled SFTP download automation job was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Scheduled SFTP download automation job failed.");
        }
        finally
        {
            _jobLock.Release();
        }
    }
}
