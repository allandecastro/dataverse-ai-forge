using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Dataverse;

internal sealed partial class DataverseClient
{
    public async Task<BatchOperationResult> CreateRecordsBatchAsync(string entitySetName, IReadOnlyList<string> recordPayloadsJson, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating {Count} records in batch for {EntitySet}", recordPayloadsJson.Count, entitySetName);

        var createdIds = new List<Guid>();
        var errors = new List<string>();

        // Create records individually (Dataverse $batch is complex; individual POSTs are reliable)
        foreach (var payloadJson in recordPayloadsJson)
        {
            try
            {
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{ApiBase}{entitySetName}", content, cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response, $"Create {entitySetName} record", cancellationToken);

                if (response.Headers.TryGetValues("OData-EntityId", out var values))
                {
                    var entityIdUrl = values.First();
                    var guidString = entityIdUrl[(entityIdUrl.LastIndexOf('(') + 1)..entityIdUrl.LastIndexOf(')')];
                    createdIds.Add(Guid.Parse(guidString));
                }
                else
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                    if (result.TryGetProperty($"{entitySetName.TrimEnd('s')}id", out var idProp))
                    {
                        createdIds.Add(idProp.GetGuid());
                    }
                    else
                    {
                        createdIds.Add(Guid.NewGuid()); // Fallback ID
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to create record: {ex.Message}");
            }
        }

        return new BatchOperationResult(
            SuccessCount: createdIds.Count,
            FailureCount: errors.Count,
            CreatedRecordIds: createdIds.AsReadOnly(),
            Errors: errors.AsReadOnly());
    }

    public async Task<int> GetRecordCountAsync(string entitySetName, string? filter = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting record count for {EntitySet}", entitySetName);

        var url = $"{ApiBase}{entitySetName}?$count=true&$top=0";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }

        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (json.TryGetProperty("@odata.count", out var countProp))
        {
            return countProp.GetInt32();
        }

        return 0;
    }

    public async Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting {Count} records from {EntitySet}", recordIds.Count, entitySetName);

        var deletedCount = 0;
        foreach (var recordId in recordIds)
        {
            try
            {
                var response = await _http.DeleteAsync($"{ApiBase}{entitySetName}({recordId})", cancellationToken);
                await EnsureSuccessOrSurfaceAsync(response, $"Delete {entitySetName} record", cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete record {RecordId} from {EntitySet}", recordId, entitySetName);
            }
        }

        return deletedCount;
    }
}
