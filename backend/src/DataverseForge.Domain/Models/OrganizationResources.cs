namespace DataverseForge.Domain.Models;

/// <summary>
/// Capacity / utilization snapshot returned by the Dataverse Web API
/// <c>RetrieveOrganizationResources</c> function. Useful for the Solution
/// Architect to answer "are we close to a cap?" / "how much storage have
/// we used?" without sending the user to the admin center.
/// </summary>
public record OrganizationResources(
    int CurrentNumberOfActiveUsers,
    int MaxNumberOfActiveUsers,
    int CurrentNumberOfNonInteractiveUsers,
    int MaxNumberOfNonInteractiveUsers,
    int CurrentNumberOfCustomEntities,
    int MaxNumberOfCustomEntities,
    int CurrentNumberOfPublishedWorkflows,
    int MaxNumberOfPublishedWorkflows,
    int CurrentStorage,
    int MaxStorage);
