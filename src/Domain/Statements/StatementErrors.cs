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

    public static readonly Error CustomerNotFound = Error.NotFound(
        "Statements.CustomerNotFound",
        "The specified customer account does not exist.");
}
