namespace SFTPDownloadAutomation.Models;

public class FileProcessingSettings
{
    public string[] CsrResponseMarkers { get; set; } =
    [
        ".SRC.",
        ".DWN.",
        ".UPD.",
        ".UPL."
    ];

    public string[] CsrResponseFileDetailsUpdateMarkers { get; set; } =
    [
        ".SRC.",
        ".DWN.",
        ".UPD.",
        ".UPL."
    ];

    public AuditMessageSettings AuditMessages { get; set; } = new();
}

public class AuditMessageSettings
{
    public string CsrBatchAuditPrefix { get; set; } =
        "CSR Batch ID- {CsrBatchId} {Message}";

    public string BeforeApiCall { get; set; } =
        "API processing started for response file: {FileName}";

    public string AfterApiSuccess { get; set; } =
        "API processing completed successfully for response file: {FileName}";

    public string AfterApiFailure { get; set; } =
        "API processing failed for response file: {FileName}";

    public string CatchException { get; set; } =
        "Error processing response file: {FileName}. Error: {ErrorMessage}";

    public string ResponseFileDetailsUpdateFailure { get; set; } =
        "Failed to update CKYCResposneFileDtls for response file: {FileName}. Error: {ErrorMessage}";
}
