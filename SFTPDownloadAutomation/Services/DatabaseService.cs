using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SFTPDownloadAutomation.Models;
using System.Data;

namespace SFTPDownloadAutomation.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(
        IConfiguration configuration,
        ILogger<DatabaseService> logger)
    {
        _connectionString =
            configuration.GetConnectionString("UPDDWNLDataBase")
            ?? throw new InvalidOperationException(
                "CKYCDataBase connection string is missing.");

        _logger = logger;

    }

    public async Task<HashSet<string>> GetExistingFileNamesAsync(
        CancellationToken cancellationToken = default)
    {
        var fileNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        const string query = """
            SELECT FileName
            FROM tempFileResponseDtls;
            """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                fileNames.Add(reader.GetString(0));
            }
        }

        _logger.LogInformation(
            "Retrieved {Count} existing filenames from database.",
            fileNames.Count);

        return fileNames;
    }

    public async Task BatchInsertNewFilesAsync(
        IEnumerable<string> newFiles,
        CancellationToken cancellationToken = default)
    {
        var files = newFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation(
                "No new files available for database insertion.");

            return;
        }

        var dataTable = new DataTable();

        dataTable.Columns.Add(
            "FileName",
            typeof(string));

        dataTable.Columns.Add(
            "MoveStatus",
            typeof(string));

        foreach (string fileName in files)
        {
            dataTable.Rows.Add(
                fileName,
                "P");
        }

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            using var bulkCopy =
                new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.Default,
                    transaction);

            bulkCopy.DestinationTableName =
                "tempFileResponseDtls";

            bulkCopy.BatchSize = 5000;

            bulkCopy.ColumnMappings.Add(
                "FileName",
                "FileName");

            bulkCopy.ColumnMappings.Add(
                "MoveStatus",
                "MoveStatus");

            await bulkCopy.WriteToServerAsync(
                dataTable,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            _logger.LogInformation(
                "Successfully inserted {Count} new files with MoveStatus = P.",
                files.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _logger.LogError(
                ex,
                "Failed to batch insert {Count} new files.",
                files.Count);

            throw;
        }
    }


    public async Task<List<FileStatusRecord>> GetExistingFileRecordsAsync(
    CancellationToken cancellationToken = default)
    {
        var records = new List<FileStatusRecord>();

        const string query = """
        SELECT FileName, MoveStatus
        FROM tempFileResponseDtls;
        """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            string fileName =
                reader.GetString(0);

            string moveStatus =
                reader.IsDBNull(1)
                    ? string.Empty
                    : reader.GetString(1);

            records.Add(
                new FileStatusRecord
                {
                    FileName = fileName,
                    MoveStatus = moveStatus
                });
        }

        _logger.LogInformation(
            "Retrieved {Count} existing file records with statuses from database.",
            records.Count);

        return records;
    }



    public async Task UpdateMoveStatusAsync(
    string fileName,
    string moveStatus,
    CancellationToken cancellationToken = default)
    {
        const string query = """
        UPDATE tempFileResponseDtls
        SET MoveStatus = @MoveStatus
        WHERE FileName = @FileName;
        """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        command.Parameters.AddWithValue(
            "@FileName",
            fileName);

        command.Parameters.AddWithValue(
            "@MoveStatus",
            moveStatus);

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        _logger.LogInformation(
            "Updated {FileName} status to {MoveStatus}.",
            fileName,
            moveStatus);
    }

    public async Task UpdateMoveStatusCSRBatchAsync(
   string CSRBatchID,
   string moveStatus,
   CancellationToken cancellationToken = default)
    {
        const string query = """
         Update CKYC..MW_CSR_BATCH_DTLS  
         set status =@MoveStatus where CSRBatchID=@CSRBatchID;
        """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        command.Parameters.AddWithValue(
            "@CSRBatchID",
            CSRBatchID);

        command.Parameters.AddWithValue(
            "@MoveStatus",
            moveStatus);

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        _logger.LogInformation(
            "Updated {FileName} status to {MoveStatus}.",
            CSRBatchID,
            moveStatus);
    }

    public async Task UpdateBatchWiseAuditDetailsForFileGenerationAsync(
    string mwBatchId,
    string createdBy,
    string message,
    CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(
                "usp_InsertBatchAuditTrail",
                connection);

        command.CommandType =
            CommandType.StoredProcedure;

        command.Parameters.AddWithValue(
            "@MWBatchID",
            mwBatchId);

        command.Parameters.AddWithValue(
            "@Message",
            message);

        command.Parameters.AddWithValue(
            "@CreatedBy",
            createdBy);

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        _logger.LogInformation(
            "Inserted batch audit trail. MWBatchID = {MWBatchId}, Message = {Message}",
            mwBatchId,
            message);
    }

    public async Task<string?> GetMwBatchIdForCsrBatchIdAsync(
    string csrBatchId,
    CancellationToken cancellationToken = default)
    {
        const string query = """
        DECLARE @CsrBatchColumn sysname;
        DECLARE @Sql nvarchar(max);

        SELECT TOP (1)
            @CsrBatchColumn = c.name
        FROM ckyc.sys.columns c
        INNER JOIN ckyc.sys.objects o
            ON c.object_id = o.object_id
        WHERE o.name = 'MW_CSR_BATCH_DTLS'
          AND UPPER(c.name) IN ('CSRBATCHID', 'CRSBATCHID', 'CSRBATCHNO', 'CRSBATCHNO', 'BATCHID');

        IF @CsrBatchColumn IS NULL
        BEGIN
            SELECT CAST(NULL AS varchar(50)) AS MWBatchID;
            RETURN;
        END;

        SET @Sql = N'
            SELECT TOP (1)
                CAST(MWBatchID AS varchar(50))
            FROM ckyc..MW_CSR_BATCH_DTLS
            WHERE CAST(' + QUOTENAME(@CsrBatchColumn) + N' AS varchar(50)) = @CsrBatchId;';

        EXEC sp_executesql
            @Sql,
            N'@CsrBatchId varchar(50)',
            @CsrBatchId = @CsrBatchId;
        """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        command.Parameters.AddWithValue(
            "@CsrBatchId",
            csrBatchId);

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        string? mwBatchId =
            result == null ||
            result == DBNull.Value
                ? null
                : Convert.ToString(result);

        _logger.LogInformation(
            "CSR Batch ID {CsrBatchId} mapped to MW Batch ID {MwBatchId}.",
            csrBatchId,
            mwBatchId);

        return string.IsNullOrWhiteSpace(mwBatchId)
            ? null
            : mwBatchId;
    }

    public async Task UpdateCsrResponseFileDetailsAsync(
    string mwBatchId,
    string responseFileName,
    CancellationToken cancellationToken = default)
    {
        const string query = """
        UPDATE ckyc..MW_CSR_BATCH_DTLS
        SET CKYCResposneFileDtls = @ResponseFileName
        WHERE CAST(MWBatchID AS varchar(50)) = @MWBatchID;
        """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(query, connection);

        command.Parameters.AddWithValue(
            "@MWBatchID",
            mwBatchId);

        command.Parameters.AddWithValue(
            "@ResponseFileName",
            responseFileName);

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        _logger.LogInformation(
            "Updated CKYCResposneFileDtls for MW Batch ID {MwBatchId}. Rows affected: {AffectedRows}.",
            mwBatchId,
            affectedRows);
    }

}
