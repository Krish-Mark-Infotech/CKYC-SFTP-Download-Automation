using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFTPDownloadAutomation.Models;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace SFTPDownloadAutomation.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly ApiSettings _apiSettings;
    private readonly ILogger<ApiService> _logger;
    private readonly IConfiguration _configuration;

    public ApiService(
        HttpClient httpClient,
        IOptions<ApiSettings> apiSettings,
        ILogger<ApiService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiSettings = apiSettings.Value;
        _logger = logger;
        _configuration = configuration;
    }

    //public async Task<bool> ProcessFileAsync(
    //    string filePath,
    //    CancellationToken cancellationToken = default)
    //{
    //    try
    //    {
    //        _logger.LogInformation(
    //            "API processing started for {FilePath}.",
    //            filePath);

    //        if (string.IsNullOrWhiteSpace(_apiSettings.ProcessFileEndpoint))
    //        {
    //            _logger.LogError(
    //                "API endpoint is not configured. Set ApiSettings:ProcessFileEndpoint in appsettings.json.");

    //            return false;
    //        }

    //        if (_httpClient.BaseAddress is null &&
    //            !Uri.TryCreate(
    //                _apiSettings.ProcessFileEndpoint,
    //                UriKind.Absolute,
    //                out _))
    //        {
    //            _logger.LogError(
    //                "API BaseUrl is not configured. Set ApiSettings:BaseUrl in appsettings.json or use an absolute ProcessFileEndpoint.");

    //            return false;
    //        }

    //        await using FileStream fileStream =
    //            File.OpenRead(filePath);

    //        using var formData =
    //            new MultipartFormDataContent();

    //        using var fileContent =
    //            new StreamContent(fileStream);

    //        fileContent.Headers.ContentType =
    //            new MediaTypeHeaderValue(
    //                "application/octet-stream");

    //        formData.Add(
    //            fileContent,
    //            _apiSettings.FileFormFieldName,
    //            Path.GetFileName(filePath));

    //        using HttpResponseMessage response =
    //            await _httpClient.PostAsync(
    //                _apiSettings.ProcessFileEndpoint,
    //                formData,
    //                cancellationToken);

    //        if (!response.IsSuccessStatusCode)
    //        {
    //            string responseBody =
    //                await response.Content.ReadAsStringAsync(
    //                    cancellationToken);

    //            _logger.LogError(
    //                "API returned failure for {FilePath}. StatusCode = {StatusCode}, Response = {Response}",
    //                filePath,
    //                response.StatusCode,
    //                responseBody);

    //            return false;
    //        }

    //        _logger.LogInformation(
    //            "API processing completed for {FilePath}.",
    //            filePath);

    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(
    //            ex,
    //            "API processing failed for {FilePath}.",
    //            filePath);

    //        return false;
    //    }
    //}



    public async Task<bool> ProcessFileAsync(
       string filePath, DatabaseService databaseService,
       CancellationToken cancellationToken = default)
    {

        ParsedFileInfo obj2=new ParsedFileInfo();
        try
        {
            

            using var formData =
                new MultipartFormDataContent();

            

            string Filename = Path.GetFileName(filePath);


            if (!TryParse(Filename
                  ,
                  out ParsedFileInfo? parsedFile,
                  out string errorCode,
                  out string errorMessage))
            {

                await databaseService.UpdateMoveStatusCSRBatchAsync(parsedFile.BatchId, "RF", cancellationToken);
                _logger.LogWarning(
                    "File validation failed. FileName={FileName}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                    Filename,
                    errorCode,
                    errorMessage);

            }

            obj2 = parsedFile;
            await databaseService.UpdateMoveStatusCSRBatchAsync(parsedFile.BatchId, "RI", cancellationToken);


            string? rootOutputPath =
    _configuration["OutputPaths:RootOutputpath"];

            if (string.IsNullOrWhiteSpace(rootOutputPath))
            {
                _logger.LogError(
                    "OutputPaths:RootOutputpath is missing in appsettings.json.");

                return false;
            }

            // Example: C:\CKYC 2.0\OutputCSR\Search\30875
            string destinationFolder = Path.Combine(
                rootOutputPath,
                parsedFile.DestinationFolder,
                parsedFile.BatchId);

            // Creates the destination folder and any missing parent folders.
            // Does nothing if the folder already exists.
            Directory.CreateDirectory(destinationFolder);

            // Append the filename.
            string destinationFilePath = Path.Combine(
                destinationFolder,
                parsedFile.FileName);

            cancellationToken.ThrowIfCancellationRequested();

            // Moves the file. Throws if a file with the same name already exists.
            File.Copy(filePath, destinationFilePath, overwrite: true);

            _logger.LogInformation(
                "File moved successfully from {SourcePath} to {DestinationPath}.",
                filePath,
                destinationFilePath);

            await databaseService.UpdateMoveStatusCSRBatchAsync(parsedFile.BatchId, "RS", cancellationToken);
            return true;
        }
        catch (Exception ex)
        {

            await databaseService.UpdateMoveStatusCSRBatchAsync(obj2.BatchId, "RF", cancellationToken);
            _logger.LogError(
                ex,
                "API processing failed for {FilePath}.",
                filePath);

            return false;
        }
    }

    private static readonly Regex FileNameRegex =
       new(
           @"^(?<prefix>.+)_(?<batchId>\d+)\.(?<type>SRC|UPL|DWN|UPD)(?:\.RES\d*)?\.(?<extension>zip|err)$",
           RegexOptions.IgnoreCase |
           RegexOptions.Compiled |
           RegexOptions.CultureInvariant);
    public bool TryParse(
           string fileName,
           out ParsedFileInfo? result,
           out string errorCode,
           out string errorMessage)
    {
        result = null;
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            errorCode = "FILENAME_MISSING";
            errorMessage = "Filename is required.";

            return false;
        }

        // Only a filename is allowed.
        // Directory paths are rejected.
        string cleanFileName =
            Path.GetFileName(fileName);

        if (!string.Equals(
                cleanFileName,
                fileName,
                StringComparison.Ordinal))
        {
            errorCode = "INVALID_FILENAME";
            errorMessage =
                "Directory paths are not allowed in the filename.";

            return false;
        }

        // Reject invalid filename characters.
        if (fileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            errorCode = "INVALID_FILENAME";
            errorMessage =
                "Filename contains invalid characters.";

            return false;
        }

        Match match =
            FileNameRegex.Match(fileName);

        if (!match.Success)
        {
            errorCode = "INVALID_FILENAME";
            errorMessage =
                "Filename does not follow the expected naming convention: " +
                "[...date]_[BatchID].[SRC|UPL|DWN][.RES].[zip|err].";

            return false;
        }

        // BatchID
        string batchId =
            match.Groups["batchId"].Value;

        if (!long.TryParse(
                batchId,
                out long numericBatchId) ||
            numericBatchId <= 0)
        {
            errorCode = "INVALID_BATCH_ID";
            errorMessage =
                $"Invalid BatchID: {batchId}.";

            return false;
        }

        // TYPE is the token immediately after BatchID.
        string fileType =
            match.Groups["type"].Value.ToUpperInvariant();

        // Final extension is zip or err.
        string extension =
            match.Groups["extension"].Value.ToLowerInvariant();

        string destinationFolder =
            fileType switch
            {
                "SRC" => "Search",
                "UPL" => "Registration",
                "DWN" => "Download",
                "UPD"  => "Update",
                _ => string.Empty
            };

        if (string.IsNullOrEmpty(destinationFolder))
        {
            errorCode = "UNSUPPORTED_FILE_TYPE";
            errorMessage =
                $"Unsupported file type: {fileType}.";

            return false;
        }

        result = new ParsedFileInfo
        {
            FileName = fileName,
            FileType = fileType,
            BatchId = batchId,
            DestinationFolder = destinationFolder
        };

        return true;
    }
}

public class ParsedFileInfo
{
    public string FileName { get; init; } = string.Empty;

    public string FileType { get; init; } = string.Empty;

    public string BatchId { get; init; } = string.Empty;

    public string DestinationFolder { get; init; } = string.Empty;
}
