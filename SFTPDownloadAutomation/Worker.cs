using Microsoft.Data.SqlClient;              // <-- ADDED
using Microsoft.Extensions.Configuration;

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
    private readonly IConfiguration _configuration; //add by sahil on 13nov26

    public Worker(
        FileProcessingService fileProcessingService,
        IOptions<SchedulerSettings> schedulerSettings,
        ILogger<Worker> logger,
        IConfiguration configuration)
    {
        _fileProcessingService = fileProcessingService;
        _schedulerSettings = schedulerSettings.Value;
        _logger = logger;
         _configuration = configuration; //add by sahil on 13nov26
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

        DateTime startTime = DateTime.Now; //add by sahil on 13nov26

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
            //add by sahil on 13nov26
            try
            {
                DateTime endTime = DateTime.Now;

                using SqlConnection conn = new SqlConnection(
                    _configuration.GetConnectionString("UPDDWNLDataBase"));

                using SqlCommand cmd = new SqlCommand(
                    "INSERT INTO MST_JOB_TRCKNG_DTLS (JB_NAME, STRT_TM, END_TM, DURATN) VALUES ('SFTPDownloadAutomation', @start, @end, @dur)",
                    conn);

                cmd.Parameters.AddWithValue("@start", startTime);
                cmd.Parameters.AddWithValue("@end", endTime);
                cmd.Parameters.AddWithValue("@dur", endTime - startTime); // TimeSpan -> SQL TIME

                conn.Open();
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (Exception trackEx)
            {
                _logger.LogError(trackEx, "Failed to log job tracking to MST_JOB_TRCKNG_DTLS.");
            }
            //end by sahil on 13nov26
            _jobLock.Release();
        }
    }
}
