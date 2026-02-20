namespace DVForge.Domain.Models;

public class ProjectState
{
    // ─── IDENTITY ───
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    // ─── ONBOARDING ───
    public OnboardingStatus Onboarding { get; set; } = OnboardingStatus.NotStarted;

    // ─── ENVIRONMENT ───
    public string EnvironmentUrl { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public int BaseLanguageLcid { get; set; }
    public string DataverseVersion { get; set; } = string.Empty;
    public bool EnvironmentConfirmed { get; set; }

    // ─── SOLUTION ───
    public string SolutionUniqueName { get; set; } = string.Empty;
    public string SolutionDisplayName { get; set; } = string.Empty;
    public string SolutionVersion { get; set; } = string.Empty;
    public Guid SolutionId { get; set; }
    public bool SolutionConfirmed { get; set; }

    // ─── PUBLISHER ───
    public string PublisherDisplayName { get; set; } = string.Empty;
    public string PublisherUniqueName { get; set; } = string.Empty;
    public string PublisherPrefix { get; set; } = string.Empty;
    public int PublisherOptionValuePrefix { get; set; }
    public Guid PublisherId { get; set; }

    // ─── CREATED ARTIFACTS ───
    public List<CreatedTable> Tables { get; set; } = [];
    public List<CreatedField> Fields { get; set; } = [];
    public List<CreatedRelationship> Relationships { get; set; } = [];
    public List<CreatedOptionSet> OptionSets { get; set; } = [];
    public List<CreatedForm> Forms { get; set; } = [];
    public List<CreatedView> Views { get; set; } = [];
    public List<CreatedPlugin> Plugins { get; set; } = [];
    public List<CreatedBpf> BusinessProcessFlows { get; set; } = [];
    public List<CreatedBusinessRule> BusinessRules { get; set; } = [];
    public List<CreatedSecurityRole> SecurityRoles { get; set; } = [];
    public List<CreatedRecord> SeedDataRecords { get; set; } = [];

    // ─── OPERATION HISTORY (for undo) ───
    public Stack<ExecutedOperation> OperationStack { get; set; } = new();

    // ─── PENDING ───
    public List<ProposedChange> PendingChanges { get; set; } = [];

    // ─── AUDIT ───
    public List<AuditEntry> AuditLog { get; set; } = [];

    // ─── SESSION ───
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string ConversationSummary { get; set; } = string.Empty;

    // ─── CONVENIENCE ───
    public bool IsReady => EnvironmentConfirmed && SolutionConfirmed;
    public string FullPrefix => $"{PublisherPrefix}_";
}

public enum OnboardingStatus
{
    NotStarted,
    EnvironmentConfirmed,
    SolutionSelected,
    SolutionCreated,
    Ready
}
