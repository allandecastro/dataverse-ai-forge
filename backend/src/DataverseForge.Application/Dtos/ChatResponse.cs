using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Dtos;

public record ChatResponse(string SessionId, string Response, ProjectState ProjectState);
