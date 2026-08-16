using System.ComponentModel.DataAnnotations;

namespace Web.Api.Features.Statements.Consolidated;

public sealed class ConsolidationOptions
{
    public const string SectionName = "Consolidation";

    // Maximum number of months allowed in a single consolidated request.
    [Range(1, 36)]
    public int MaxMonths { get; init; } = 12;

    // How long a generated consolidated PDF is cached.
    [Range(1, 1440)]
    public int CacheTtlMinutes { get; init; } = 60;

    // Merges larger than this are streamed but not cached (keeps large blobs out of Redis).
    [Range(0, long.MaxValue)]
    public long MaxCacheableBytes { get; init; } = 20L * 1024 * 1024;

    public TimeSpan CacheTtl => TimeSpan.FromMinutes(CacheTtlMinutes);
}
