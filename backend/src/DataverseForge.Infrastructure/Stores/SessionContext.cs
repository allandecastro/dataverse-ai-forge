using DataverseForge.Application.Contracts;

namespace DataverseForge.Infrastructure.Stores;

public class SessionContext : ISessionContext
{
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
}
