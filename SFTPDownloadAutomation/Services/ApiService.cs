using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFTPDownloadAutomation.Models;
using System.Net.Http.Headers;

namespace SFTPDownloadAutomation.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly ApiSettings _apiSettings;
    private readonly ILogger<ApiService> _logger;

    public ApiService(
        HttpClient httpClient,
        IOptions<ApiSettings> apiSettings,
        ILogger<ApiService> logger)
    {
        _httpClient = httpClient;
        _apiSettings = apiSettings.Value;
        _logger = logger;
    }

    public async Task<bool> ProcessFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "API processing started for {FilePath}.",
                filePath);

            if (string.IsNullOrWhiteSpace(_apiSettings.ProcessFileEndpoint))
            {
                _logger.LogError(
                    "API endpoint is not configured. Set ApiSettings:ProcessFileEndpoint in appsettings.json.");

                return false;
            }

            if (_httpClient.BaseAddress is null &&
                !Uri.TryCreate(
                    _apiSettings.ProcessFileEndpoint,
                    UriKind.Absolute,
                    out _))
            {
                _logger.LogError(
                    "API BaseUrl is not configured. Set ApiSettings:BaseUrl in appsettings.json or use an absolute ProcessFileEndpoint.");

                return false;
            }

            await using FileStream fileStream =
                File.OpenRead(filePath);

            using var formData =
                new MultipartFormDataContent();

            using var fileContent =
                new StreamContent(fileStream);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue(
                    "application/octet-stream");

            formData.Add(
                fileContent,
                _apiSettings.FileFormFieldName,
                Path.GetFileName(filePath));

            using HttpResponseMessage response =
                await _httpClient.PostAsync(
                    _apiSettings.ProcessFileEndpoint,
                    formData,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                _logger.LogError(
                    "API returned failure for {FilePath}. StatusCode = {StatusCode}, Response = {Response}",
                    filePath,
                    response.StatusCode,
                    responseBody);

                return false;
            }

            _logger.LogInformation(
                "API processing completed for {FilePath}.",
                filePath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "API processing failed for {FilePath}.",
                filePath);

            return false;
        }
    }
}
