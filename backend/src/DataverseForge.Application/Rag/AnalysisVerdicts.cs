namespace DataverseForge.Application.Rag;

/// <summary>How well a work-item set covers a single requirement.</summary>
public enum CoverageStatus
{
    /// <summary>At least one work item fully addresses this requirement.</summary>
    Covered,
    /// <summary>Work items address part of this requirement but not completely.</summary>
    PartiallyCovers,
    /// <summary>No work item covers this requirement.</summary>
    NotCovered
}

/// <summary>LLM verdict for a single requirement's coverage in a target source.</summary>
public sealed record CoverageVerdict(
    CoverageStatus Status,
    string Explanation,
    string? CoveringItemSummary = null);

/// <summary>How two passages relate to each other.</summary>
public enum ConflictRelation
{
    /// <summary>The two passages make conflicting claims about the same subject.</summary>
    Contradiction,
    /// <summary>Both passages are consistent but describe different aspects.</summary>
    Complement,
    /// <summary>The passages cover unrelated subjects.</summary>
    Unrelated
}

/// <summary>LLM verdict for a conflict candidate pair.</summary>
public sealed record ConflictVerdict(
    ConflictRelation Relation,
    string Explanation);
