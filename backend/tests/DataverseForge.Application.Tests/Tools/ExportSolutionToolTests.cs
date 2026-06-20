using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tests.Fakes;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExportSolutionToolTests : IDisposable
{
    private const string SessionId = "export-solution-session";
    private const string ConcurrentTool = "ConcurrentWrite";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly CloningProjectStateStore _store = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();
    private readonly string _exportDir = Path.Combine(Path.GetTempPath(), "dvforge-export-tests-" + Guid.NewGuid().ToString("N"));

    public ExportSolutionToolTests()
    {
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(SessionId);
        Directory.CreateDirectory(_exportDir);
    }

    private ExportSolutionTool CreateTool() =>
        new(_mockDataverseClient.Object, _store, _mockSessionContext.Object,
            "http://localhost:5000", () => _exportDir, NullLogger<ExportSolutionTool>.Instance);

    [Fact]
    public async Task ExportSolution_SuccessPath_PreservesConcurrentWriteLandedDuringExport()
    {
        _mockDataverseClient
            .Setup(c => c.ExportSolutionAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _store.SimulateConcurrentWrite(SessionId, s =>
                    s.AuditLog.Add(AuditEntry.Ok(AgentNames.Tester, ConcurrentTool, "landed mid-export")));
                return [1, 2, 3, 4];
            });

        await CreateTool().ExportSolutionAsync("cr123_solution", "unmanaged", publishFirst: false, CancellationToken.None);

        var saved = await _store.GetOrCreateAsync(SessionId);
        Assert.Contains(saved.AuditLog, e => e.Action == ConcurrentTool);       // not clobbered
        Assert.Contains(saved.AuditLog, e => e.Action == "ExportSolution");      // own entry persisted
    }

    [Fact]
    public async Task ExportSolution_FailurePath_PreservesConcurrentWrite_AndRecordsFailure()
    {
        _mockDataverseClient
            .Setup(c => c.ExportSolutionAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _store.SimulateConcurrentWrite(SessionId, s =>
                    s.AuditLog.Add(AuditEntry.Ok(AgentNames.Tester, ConcurrentTool, "landed mid-export")));
                throw new InvalidOperationException("export boom");
            });

        var result = await CreateTool().ExportSolutionAsync("cr123_solution", "unmanaged", publishFirst: false, CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        var saved = await _store.GetOrCreateAsync(SessionId);
        Assert.Contains(saved.AuditLog, e => e.Action == ConcurrentTool);
        Assert.Contains(saved.AuditLog, e => e.Action == "ExportSolution" && !e.Success);
    }

    public void Dispose()
    {
        try { Directory.Delete(_exportDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
