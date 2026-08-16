using FluentValidation;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Web.Api.Features.Statements.Upload;

internal sealed class UploadStatementCommandValidator : AbstractValidator<UploadStatementCommand>
{
    private const string PdfContentType = "application/pdf";

    public UploadStatementCommandValidator(IOptions<StorageOptions> storageOptions)
    {
        long maxBytes = storageOptions.Value.MaxUploadBytes;
        long maxMegabytes = maxBytes / (1024 * 1024);

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
            .Must(s => s.Length <= maxBytes)
            .WithMessage($"File size cannot exceed {maxMegabytes} MB.");

        RuleFor(c => c.Period)
            .NotEmpty()
            .Matches(@"^\d{4}-(0[1-9]|1[0-2])$")
            .WithMessage("Period must be in YYYY-MM format, e.g. 2024-01.");

        RuleFor(c => c.Description)
            .MaximumLength(500);
    }
}
