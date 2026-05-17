using FluentValidation;

namespace Application.Statements.Upload;

internal sealed class UploadStatementCommandValidator : AbstractValidator<UploadStatementCommand>
{
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const string PdfContentType = "application/pdf";

    public UploadStatementCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();

        RuleFor(c => c.OriginalFileName)
            .NotEmpty()
            .MaximumLength(255)
            .Must(name => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PDF files are accepted.");

        RuleFor(c => c.ContentType)
            .NotEmpty()
            .Must(ct => ct.Equals(PdfContentType, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Content-Type must be application/pdf.");

        RuleFor(c => c.FileContent)
            .NotNull()
            .Must(s => s.Length > 0)
            .WithMessage("File content cannot be empty.")
            .Must(s => s.Length <= MaxFileSizeBytes)
            .WithMessage($"File size cannot exceed 50 MB.");

        RuleFor(c => c.Period)
            .NotEmpty()
            .Matches(@"^\d{4}-(0[1-9]|1[0-2])$")
            .WithMessage("Period must be in YYYY-MM format, e.g. 2024-01.");

        RuleFor(c => c.Description)
            .MaximumLength(500);
    }
}
