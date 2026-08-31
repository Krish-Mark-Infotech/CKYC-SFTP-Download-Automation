using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFTPDownloadAutomation.Models;

namespace SFTPDownloadAutomation.Services;

public class SFTPService
{
    private readonly SFTPSettings _settings;
    private readonly ILogger<SFTPService> _logger;

    public SFTPService(
        IOptions<SFTPSettings> settings,
        ILogger<SFTPService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> ExecuteDownloadAsync(
        CancellationToken cancellationToken = default)
    {
        // Check SFTP executable
        if (!File.Exists(_settings.RunnerPath))
        {
            _logger.LogError(
                "SFTP Runner not found: {RunnerPath}",
                _settings.RunnerPath);

            return false;
        }

        // Check download folder
        if (!Directory.Exists(_settings.DownloadPath))
        {
            _logger.LogError(
                "Download folder not found: {DownloadPath}",
                _settings.DownloadPath);

            return false;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _settings.RunnerPath,

            // Executes:
            // SFTPRunner.exe download
            Arguments = "download",

            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process
        {
            StartInfo = processStartInfo
        };

        try
        {
            _logger.LogInformation(
                "Starting SFTP download...");

            _logger.LogInformation(
                "Runner Path: {RunnerPath}",
                _settings.RunnerPath);

            if (!process.Start())
            {
                _logger.LogError(
                    "Failed to start SFTP Runner.");

                return false;
            }

            // Read output and error asynchronously
            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            // IMPORTANT:
            // Wait until SFTP process completely finishes
            await process.WaitForExitAsync(
                cancellationToken);

            string output =
                await outputTask;

            string error =
                await errorTask;

            _logger.LogInformation(
                "SFTP process finished. Exit Code: {ExitCode}",
                process.ExitCode);

            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger.LogInformation(
                    "SFTP Output: {Output}",
                    output);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning(
                    "SFTP Error Output: {Error}",
                    error);
            }

            // Exit code 0 = successful execution
            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "SFTP download failed. Exit Code: {ExitCode}",
                    process.ExitCode);

                return false;
            }

            _logger.LogInformation(
                "SFTP download completed successfully.");

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "SFTP download was cancelled.");

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception occurred while executing SFTP download.");

            return false;
        }
    }
}