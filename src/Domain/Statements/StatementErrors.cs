using SharedKernel;

namespace Domain.Statements;

public static class StatementErrors
{
    public static Error NotFound(Guid statementId) => Error.NotFound(
        "Statements.NotFound",
        $"Statement '{statementId}' was not found.");

    public static readonly Error AlreadyRevoked = Error.Conflict(
        "Statements.AlreadyRevoked",
        "This statement has already been revoked.");

    public static Error ActiveStatementExistsForPeriod(string period) => Error.Conflict(
        "Statements.ActiveStatementExistsForPeriod",
        $"A statement already exists for this customer for period {period}. Revoke the existing " +
        "statement before uploading a replacement.");

    public static readonly Error AccessDenied = Error.Failure(
        "Statements.AccessDenied",
        "You do not have access to this statement.");

    public static readonly Error InvalidFileType = Error.Problem(
        "Statements.InvalidFileType",
        "Only PDF files are accepted. Ensure the file is a valid PDF.");

    public static readonly Error FileTooLarge = Error.Problem(
        "Statements.FileTooLarge",
        "The uploaded file exceeds the maximum allowed size of 50 MB.");

    public static readonly Error InvalidPeriodFormat = Error.Problem(
        "Statements.InvalidPeriodFormat",
        "Period must be in YYYY-MM format, e.g. 2024-01.");

    public static readonly Error InvalidPeriodRange = Error.Problem(
        "Statements.InvalidPeriodRange",
        "Invalid period range: 'from' must be on or before 'to' and span at most 12 months.");

    public static readonly Error NoStatementsInRange = Error.NotFound(
        "Statements.NoStatementsInRange",
        "No statements were found for the selected period range.");

    public static readonly Error CustomerNotFound = Error.NotFound(
        "Statements.CustomerNotFound",
        "The specified customer account does not exist.");

    public static readonly Error InvalidFileContent = Error.Problem(
        "Statements.InvalidFileContent",
        "The uploaded file does not appear to be a valid PDF. File content validation failed.");

    public static readonly Error MalwareDetected = Error.Problem(
        "Statements.MalwareDetected",
        "The uploaded file failed the security scan and was rejected.");
}
