using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetFormDefinitionTool(IDataverseClient dataverseClient)
{
    [Description("Get the full definition of a specific form including its FormXML. Use this to inspect existing form layout before creating or modifying forms.")]
    public async Task<FormDefinition> GetFormDefinitionAsync(
        [Description("The GUID of the form to retrieve")] string formId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetFormDefinitionAsync(Guid.Parse(formId), cancellationToken);
    }
}
