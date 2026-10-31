using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFTPDownloadAutomation.Models;

namespace SFTPDownloadAutomation.Services;

public class FileProcessingService
{
    private readonly SFTPService _sftpService;
    private readonly DatabaseService _databaseService;
    private readonly SFTPSettings _sftpSettings;
    private readonly ILogger<FileProcessingService> _logger;
    private readonly ApiService _apiService;
    private readonly ApiSettings _apiSettings;
    private readonly FileProcessingSettings _fileProcessingSettings;

    public FileProcessingService(
        SFTPService sftpService,
        DatabaseService databaseService,
        ApiService apiService,
        IOptions<SFTPSettings> sftpSettings,
        IOptions<ApiSettings> apiSettings,
        IOptions<FileProcessingSettings> fileProcessingSettings,
        ILogger<FileProcessingService> logger)
    {
        _sftpService = sftpService;
        _databaseService = databaseService;
        _sftpSettings = sftpSettings.Value;
        _apiService = apiService;
        _apiSettings = apiSettings.Value;
        _fileProcessingSettings = fileProcessingSettings.Value;
        _logger = logger;
    }


    public async Task RunAsync(
    CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "==============================================");

            _logger.LogInformation(
                "SFTP DOWNLOAD AUTOMATION STARTED");

            _logger.LogInformation(
                "==============================================");

            // ==================================================
            // STEP 1
            // Execute SFTP download
            // ==================================================

            _logger.LogInformation(
                "Step 1: Starting SFTP download...");

            bool sftpSuccess =
                await _sftpService.ExecuteDownloadAsync(
                    cancellationToken);

            if (!sftpSuccess)
            {
                _logger.LogError(
                    "SFTP download failed.");

                _logger.LogError(
                    "Stopping file processing.");

                return;
            }

            _logger.LogInformation(
                "Step 1: SFTP download completed successfully.");

            // ==================================================
            // STEP 2
            // Get all filenames from download folder
            // ==================================================

            _logger.LogInformation(
                "Step 2: Reading files from download folder...");

            List<string> folderFileNames =
                GetDownloadFolderFiles();

            _logger.LogInformation(
                "Files found in download folder: {Count}",
                folderFileNames.Count);

            if (folderFileNames.Count == 0)
            {
                _logger.LogInformation(
                    "No files found in download folder.");

                return;
            }

            // ==================================================
            // STEP 3
            // Get existing DB records
            // FileName + MoveStatus
            // ==================================================

            _logger.LogInformation(
                "Step 3: Reading existing file records from database...");

            List<FileStatusRecord> dbRecords =
                await _databaseService.GetExistingFileRecordsAsync(
                    cancellationToken);

            _logger.LogInformation(
                "Database records retrieved: {Count}",
                dbRecords.Count);

            // ==================================================
            // STEP 4
            // Create DB filename lookup
            // ==================================================

            var dbFileNames =
                dbRecords
                    .Select(x => x.FileName)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            // ==================================================
            // STEP 5
            // Identify NEW files
            // ==================================================

            List<string> newFiles =
                folderFileNames
                    .Where(fileName =>
                        !dbFileNames.Contains(fileName))
                    .ToList();

            _logger.LogInformation(
                "New files identified: {Count}",
                newFiles.Count);

            // ==================================================
            // STEP 6
            // Identify PENDING files
            // ==================================================

            HashSet<string> pendingFiles =
                dbRecords
                    .Where(x =>
                        x.MoveStatus.Equals(
                            "P",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.FileName)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Pending files identified: {Count}",
                pendingFiles.Count);

            // ==================================================
            // STEP 7
            // Identify FAILED files
            // ==================================================

            HashSet<string> failedFiles =
                dbRecords
                    .Where(x =>
                        x.MoveStatus.Equals(
                            "F",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.FileName)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Failed files identified: {Count}",
                failedFiles.Count);

            // ==================================================
            // STEP 8
            // Create FINAL FilesToProcess
            //
            // New + P + F
            // ==================================================

            HashSet<string> filesToProcess =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            // Add NEW files
            foreach (string fileName in newFiles)
            {
                filesToProcess.Add(fileName);
            }

            // Add PENDING files
            foreach (string fileName in pendingFiles)
            {
                filesToProcess.Add(fileName);
            }

            // Add FAILED files
            foreach (string fileName in failedFiles)
            {
                filesToProcess.Add(fileName);
            }

            _logger.LogInformation(
                "Total FilesToProcess: {Count}",
                filesToProcess.Count);

            if (filesToProcess.Count == 0)
            {
                _logger.LogInformation(
                    "No files require API processing.");

                return;
            }

            // ==================================================
            // STEP 9
            // Batch insert ONLY NEW files
            // New files start with P
            // ==================================================

            if (newFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Batch inserting {Count} new files with MoveStatus = P.",
                    newFiles.Count);

                await _databaseService.BatchInsertNewFilesAsync(
                    newFiles,
                    cancellationToken);

                _logger.LogInformation(
                    "New files inserted successfully.");
            }
            else
            {
                _logger.LogInformation(
                    "No new files to insert.");
            }

            // ==================================================
            // STEP 10
            // Verify actual files exist and are stable
            // ==================================================

            _logger.LogInformation(
                "Step 10: Verifying files before API processing...");

            var validFilesToProcess =
                new List<string>();

            foreach (string fileName in filesToProcess)
            {
                string filePath =
                    Path.Combine(
                        _sftpSettings.DownloadPath,
                        fileName);

                // Check file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning(
                        "File not found in download folder: {FileName}",
                        fileName);

                    continue;
                }

                // Check file is stable
                bool isStable =
                    await IsFileStableAsync(
                        filePath,
                        cancellationToken);

                if (!isStable)
                {
                    _logger.LogWarning(
                        "File is not stable or is still being written: {FileName}",
                        fileName);

                    continue;
                }

                validFilesToProcess.Add(fileName);
            }

            _logger.LogInformation(
                "Files available for API processing: {Count}",
                validFilesToProcess.Count);

            if (validFilesToProcess.Count == 0)
            {
                _logger.LogInformation(
                    "No valid files available for API processing.");

                return;
            }

            // ==================================================
            // STEP 11
            // Controlled parallel API processing
            // ==================================================

            int maxParallelism =
                Math.Max(
                    1,
                    _apiSettings.MaxParallelism);

            _logger.LogInformation(
                "Starting API processing. MaxParallelism = {MaxParallelism}",
                maxParallelism);

            using var semaphore =
                new SemaphoreSlim(maxParallelism);

            var processingTasks =
                validFilesToProcess.Select(
                    async fileName =>
                    {
                        await semaphore.WaitAsync(
                            cancellationToken);

                        BatchAuditContext batchAuditContext =
                            CreateDefaultBatchAuditContext(
                                fileName);

                        try
                        {
                            string filePath =
                                Path.Combine(
                                    _sftpSettings.DownloadPath,
                                    fileName);

                            batchAuditContext =
                                await GetBatchAuditContextAsync(
                                    fileName,
                                    cancellationToken);

                            // ------------------------------------------
                            // P/F → I
                            // ------------------------------------------

                            await _databaseService.UpdateMoveStatusAsync(
                                fileName,
                                "I",
                                cancellationToken);

                            
                            _logger.LogInformation(
                                "File marked In Progress: {FileName}",
                                fileName);

                            // ------------------------------------------
                            // API
                            // ------------------------------------------

                            await UpdateCsrResponseFileDetailsAsync(
                                batchAuditContext,
                                fileName,
                                cancellationToken);

                            await WriteBatchAuditAsync(
                                batchAuditContext,
                                FormatAuditMessage(
                                    _fileProcessingSettings.AuditMessages.BeforeApiCall,
                                    fileName),
                                cancellationToken);

                            bool success =
                                await _apiService.ProcessFileAsync(
                                    filePath,_databaseService,
                                    cancellationToken);

                            // ------------------------------------------
                            // API SUCCESS
                            // I → S
                            // ------------------------------------------

                            if (success)
                            {
                                await _databaseService.UpdateMoveStatusAsync(
                                    fileName,
                                    "S",
                                    cancellationToken);

                                await WriteBatchAuditAsync(
                                    batchAuditContext,
                                    FormatAuditMessage(
                                        _fileProcessingSettings.AuditMessages.AfterApiSuccess,
                                        fileName),
                                    cancellationToken);

                                _logger.LogInformation(
                                    "File processed successfully: {FileName}",
                                    fileName);
                            }
                            else
                            {
                                // --------------------------------------
                                // API FAILURE
                                // I → F
                                // --------------------------------------

                                await _databaseService.UpdateMoveStatusAsync(
                                    fileName,
                                    "F",
                                    cancellationToken);

                                await WriteBatchAuditAsync(
                                    batchAuditContext,
                                    FormatAuditMessage(
                                        _fileProcessingSettings.AuditMessages.AfterApiFailure,
                                        fileName),
                                    cancellationToken);

                                _logger.LogError(
                                    "API processing failed: {FileName}",
                                    fileName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Error processing file: {FileName}",
                                fileName);

                            // ------------------------------------------
                            // Any processing exception
                            // → F
                            // ------------------------------------------

                            try
                            {
                                await _databaseService.UpdateMoveStatusAsync(
                                    fileName,
                                    "F",
                                    cancellationToken);
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogError(
                                    dbEx,
                                    "Failed to update MoveStatus = F for {FileName}",
                                    fileName);
                            }

                            await WriteBatchAuditAsync(
                                batchAuditContext,
                                FormatAuditMessage(
                                    _fileProcessingSettings.AuditMessages.CatchException,
                                    fileName,
                                    ex.Message),
                                cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

            await Task.WhenAll(processingTasks);

            // ==================================================
            // STEP 12
            // Processing completed
            // ==================================================

            _logger.LogInformation(
                "API processing completed for all eligible files.");

            _logger.LogInformation(
                "==============================================");

            _logger.LogInformation(
                "SFTP DOWNLOAD AUTOMATION COMPLETED");

            _logger.LogInformation(
                "==============================================");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "File processing was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error occurred during file processing.");

            throw;
        }
    }
    //public async Task RunAsync(
    //    CancellationToken cancellationToken = default)
    //{
    //    try
    //    {
    //        _logger.LogInformation(
    //            "==============================================");

    //        _logger.LogInformation(
    //            "SFTP DOWNLOAD AUTOMATION STARTED");

    //        _logger.LogInformation(
    //            "==============================================");

    //        // ==================================================
    //        // STEP 1 & 2
    //        // Execute SFTP download and WAIT for completion
    //        // ==================================================

    //        _logger.LogInformation(
    //            "Step 1: Starting SFTP download...");

    //        bool sftpSuccess =
    //            await _sftpService.ExecuteDownloadAsync(
    //                cancellationToken);

    //        if (!sftpSuccess)
    //        {
    //            _logger.LogError(
    //                "SFTP download failed.");

    //            _logger.LogError(
    //                "Stopping file processing.");

    //            return;
    //        }

    //        _logger.LogInformation(
    //            "Step 2: SFTP download completed successfully.");

    //        // ==================================================
    //        // STEP 3
    //        // Get all filenames from download folder
    //        // ==================================================

    //        _logger.LogInformation(
    //            "Step 3: Reading files from download folder...");

    //        List<string> folderFileNames =
    //            GetDownloadFolderFiles();

    //        _logger.LogInformation(
    //            "Found {Count} files in download folder.",
    //            folderFileNames.Count);

    //        if (folderFileNames.Count == 0)
    //        {
    //            _logger.LogInformation(
    //                "No files found in download folder.");

    //            return;
    //        }

    //        // ==================================================
    //        // STEP 4
    //        // Get all existing filenames from database
    //        // ==================================================

    //        _logger.LogInformation(
    //            "Step 4: Reading existing filenames from database...");

    //        HashSet<string> dbFileNames =
    //            await _databaseService.GetExistingFileNamesAsync(
    //                cancellationToken);

    //        _logger.LogInformation(
    //            "Database contains {Count} filenames.",
    //            dbFileNames.Count);

    //        // ==================================================
    //        // STEP 5
    //        // Folder Files - DB Files
    //        // ==================================================

    //        _logger.LogInformation(
    //            "Step 5: Comparing folder files with database files...");

    //        List<string> newFiles =
    //            folderFileNames
    //                .Where(fileName =>
    //                    !dbFileNames.Contains(fileName))
    //                .ToList();

    //        _logger.LogInformation(
    //            "Folder files       : {FolderCount}",
    //            folderFileNames.Count);

    //        _logger.LogInformation(
    //            "Database files     : {DatabaseCount}",
    //            dbFileNames.Count);

    //        _logger.LogInformation(
    //            "New files identified: {NewFileCount}",
    //            newFiles.Count);

    //        // ==================================================
    //        // No new files
    //        // ==================================================

    //        if (newFiles.Count == 0)
    //        {
    //            _logger.LogInformation(
    //                "No new files found. Nothing to insert.");

    //            return;
    //        }

    //        // ==================================================
    //        // STEP 6
    //        // Batch insert ONLY new files
    //        // ==================================================

    //        _logger.LogInformation(
    //            "Step 6: Batch inserting new files...");

    //        await _databaseService.BatchInsertNewFilesAsync(
    //            newFiles,
    //            cancellationToken);

    //        _logger.LogInformation(
    //            "Successfully processed {Count} new files.",
    //            newFiles.Count);

    //        _logger.LogInformation(
    //            "==============================================");

    //        _logger.LogInformation(
    //            "PHASE 1 COMPLETED");

    //        _logger.LogInformation(
    //            "API processing will be added later.");

    //        _logger.LogInformation(
    //            "==============================================");
    //    }
    //    catch (OperationCanceledException)
    //    {
    //        _logger.LogWarning(
    //            "File processing was cancelled.");
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(
    //            ex,
    //            "Unexpected error occurred during file processing.");

    //        throw;
    //    }
    //}



    private async Task<bool> IsFileStableAsync(
    string filePath,
    CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            long previousSize =
                new FileInfo(filePath).Length;

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            long currentSize =
                new FileInfo(filePath).Length;

            if (previousSize != currentSize)
            {
                return false;
            }

            using var stream =
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            return stream.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private List<string> GetDownloadFolderFiles()
    {
        if (!Directory.Exists(_sftpSettings.DownloadPath))
        {
            throw new DirectoryNotFoundException(
                $"Download folder does not exist: " +
                $"{_sftpSettings.DownloadPath}");
        }

        return Directory
            .EnumerateFiles(
                _sftpSettings.DownloadPath,
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(fileName =>
                !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
    }

    private async Task<BatchAuditContext> GetBatchAuditContextAsync(
    string fileName,
    CancellationToken cancellationToken)
    {
        BatchAuditContext context =
            CreateDefaultBatchAuditContext(
                fileName);

        if (!context.IsCsrResponseFile)
        {
            return context;
        }

        try
        {
            string? mwBatchId =
                await _databaseService.GetMwBatchIdForCsrBatchIdAsync(
                    context.SourceBatchId,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(mwBatchId))
            {
                _logger.LogWarning(
                    "MW Batch ID was not found for CSR Batch ID {CsrBatchId}. Audit will use CSR Batch ID.",
                    context.SourceBatchId);

                return context;
            }

            return new BatchAuditContext(
                context.SourceBatchId,
                mwBatchId,
                IsCsrResponseFile: true,
                ShouldUpdateCsrResponseFileDetails: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get MW Batch ID for CSR Batch ID {CsrBatchId}. Audit will use CSR Batch ID.",
                context.SourceBatchId);

            return context;
        }
    }

    private async Task UpdateCsrResponseFileDetailsAsync(
    BatchAuditContext context,
    string fileName,
    CancellationToken cancellationToken)
    {
        if (!context.ShouldUpdateCsrResponseFileDetails)
        {
            return;
        }

        try
        {
            await _databaseService.UpdateCsrResponseFileDetailsAsync(
                context.AuditBatchId,
                fileName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update CKYCResposneFileDtls for {FileName}.",
                fileName);

            await WriteBatchAuditAsync(
                context,
                FormatAuditMessage(
                    _fileProcessingSettings.AuditMessages.ResponseFileDetailsUpdateFailure,
                    fileName,
                    ex.Message),
                cancellationToken);
        }
    }

    private async Task WriteBatchAuditAsync(
    BatchAuditContext context,
    string message,
    CancellationToken cancellationToken)
    {
        string auditMessage =
            context.IsCsrResponseFile
                ? FormatCsrBatchAuditMessage(
                    _fileProcessingSettings.AuditMessages.CsrBatchAuditPrefix,
                    context.SourceBatchId,
                    message)
                : message;

        try
        {
            await _databaseService.UpdateBatchWiseAuditDetailsForFileGenerationAsync(
                context.AuditBatchId,
                "SFTPDownloadAutomation",
                auditMessage,
                cancellationToken);
        }
        catch (Exception auditEx)
        {
            _logger.LogError(
                auditEx,
                "Failed to insert batch audit trail for BatchID {BatchId}.",
                context.AuditBatchId);
        }
    }

    private BatchAuditContext CreateDefaultBatchAuditContext(
    string fileName)
    {
        string sourceBatchId =
            GetBatchIdFromFileName(
                fileName);

        bool isCsrResponseFile =
            IsCsrResponseFile(
                fileName);

        bool shouldUpdateCsrResponseFileDetails =
            !isCsrResponseFile &&
            ShouldUpdateCsrResponseFileDetails(
                fileName);

        return new BatchAuditContext(
            sourceBatchId,
            sourceBatchId,
            isCsrResponseFile,
            shouldUpdateCsrResponseFileDetails);
    }

    private bool IsCsrResponseFile(
    string fileName)
    {
        return FileNameContainsAnyMarker(
            fileName,
            _fileProcessingSettings.CsrResponseMarkers);
    }

    private bool ShouldUpdateCsrResponseFileDetails(
    string fileName)
    {
        return FileNameContainsAnyMarker(
            fileName,
            _fileProcessingSettings.CsrResponseFileDetailsUpdateMarkers);
    }

    private static bool FileNameContainsAnyMarker(
    string fileName,
    IEnumerable<string>? markers)
    {
        return markers?
            .Where(marker =>
                !string.IsNullOrWhiteSpace(marker))
            .Any(marker =>
                fileName.Contains(
                    marker,
                    StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string FormatAuditMessage(
    string? template,
    string fileName,
    string? errorMessage = null)
    {
        string messageTemplate =
            string.IsNullOrWhiteSpace(template)
                ? "{FileName}"
                : template;

        return messageTemplate
            .Replace(
                "{FileName}",
                fileName,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{ErrorMessage}",
                errorMessage ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCsrBatchAuditMessage(
    string? template,
    string csrBatchId,
    string message)
    {
        string messageTemplate =
            string.IsNullOrWhiteSpace(template)
                ? "{Message}"
                : template;

        return messageTemplate
            .Replace(
                "{CsrBatchId}",
                csrBatchId,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{Message}",
                message,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetBatchIdFromFileName(
    string fileName)
    {
        string nameWithoutExtension =
            Path.GetFileNameWithoutExtension(fileName);

        int dotIndex =
            nameWithoutExtension.IndexOf('.');

        string nameBeforeStatus =
            dotIndex >= 0
                ? nameWithoutExtension[..dotIndex]
                : nameWithoutExtension;

        string? batchId =
            nameBeforeStatus
                .Split(
                    '_',
                    StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

        return string.IsNullOrWhiteSpace(batchId)
            ? fileName
            : batchId;
    }

    private sealed record BatchAuditContext(
        string SourceBatchId,
        string AuditBatchId,
        bool IsCsrResponseFile,
        bool ShouldUpdateCsrResponseFileDetails);
}
