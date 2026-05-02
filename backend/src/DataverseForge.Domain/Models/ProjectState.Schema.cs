namespace DataverseForge.Domain.Models;

public partial class ProjectState
{
    // ─── CREATED ARTIFACTS ───
    public List<CreatedTable> Tables { get; set; } = [];
    public List<CreatedField> Fields { get; set; } = [];
    public List<CreatedRelationship> Relationships { get; set; } = [];
    public List<CreatedOptionSet> OptionSets { get; set; } = [];
    public List<CreatedAlternateKey> AlternateKeys { get; set; } = [];
    public List<CreatedForm> Forms { get; set; } = [];
    public List<CreatedView> Views { get; set; } = [];
    public List<CreatedRecord> SeedDataRecords { get; set; } = [];
}
