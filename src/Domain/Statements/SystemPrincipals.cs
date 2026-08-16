namespace Domain.Statements;

// Well-known non-human principals recorded as the actor on machine-driven writes, so an
// ingested statement is still attributable in the audit trail even though no admin performed it.
public static class SystemPrincipals
{
    // Recorded as UploadedByAdminId when a statement enters through the automated
    // statement-generation pipeline (the M2M push API or the object-storage event worker)
    // rather than a human admin upload. Fixed and reserved so ingested statements are
    // distinguishable and filterable from human uploads.
    public static readonly Guid StatementIngestionService =
        new("00000000-0000-0000-0000-0000000000a1");
}
