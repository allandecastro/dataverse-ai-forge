using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateDashboardTool> logger)
{
    [Description("Execute the creation of an org-owned Dataverse dashboard. Pass the planId from "
        + "plan_create_dashboard — the OData JSON payload (with embedded FormXML) was stored "
        + "server-side and will be loaded automatically. Do NOT construct or pass the payload yourself.")]
    public async Task<string> ExecuteCreateDashboardAsync(
        [Description("planId returned by plan_create_dashboard — must match exactly. Payload stored server-side.")] string planId,
        [Description("Dashboard's display name (must match the plan).")] string dashboardName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateDashboard executing: planId={PlanId}, dashboardName={Name}", planId, dashboardName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_dashboard_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_dashboard. Fix the layout and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Dashboard '{dashboardName}' is already in the environment. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_create_dashboard first and pass its planId verbatim.";
        }

        try
        {
            var dashboardId = await dataverseClient.CreateDashboardAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            // Add to active solution. Dashboards use SystemForm component type
            // (24) — same as forms, since they live in the systemform table.
            //
            // Eventual-consistency retry: the create returns the systemform GUID
            // synchronously, but the AddSolutionComponent message sometimes 404s
            // because the new record hasn't propagated to the solution-component
            // index yet (real session 2026-05-05). Retrying once after a short
            // delay is enough in every case we've seen.
            var solutionAddNote = string.Empty;
            if (!string.IsNullOrWhiteSpace(state.SolutionUniqueName))
            {
                Exception? lastError = null;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await dataverseClient.AddSolutionComponentAsync(
                            dashboardId, SolutionComponentType.Form, state.SolutionUniqueName,
                            doNotIncludeSubcomponents: true, cancellationToken);
                        solutionAddNote = $" Added to solution '{state.SolutionUniqueName}'"
                            + (attempt > 1 ? $" (after {attempt} attempts — eventual consistency)." : ".");
                        lastError = null;
                        break;
                    }
                    catch (Exception solEx) when (attempt < 3 && IsTransientNotFound(solEx))
                    {
                        logger.LogInformation(
                            "AddSolutionComponent attempt {Attempt} returned not-found for dashboard {Id}; retrying",
                            attempt, dashboardId);
                        lastError = solEx;
                        await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt), cancellationToken);
                    }
                    catch (Exception solEx)
                    {
                        lastError = solEx;
                        break;
                    }
                }
                if (lastError is not null)
                {
                    logger.LogWarning(lastError, "AddSolutionComponent failed for dashboard {Name} ({DashboardId}) on solution {Solution}",
                        dashboardName, dashboardId, state.SolutionUniqueName);
                    solutionAddNote = $" WARNING: dashboard created in env but could NOT be added to solution '{state.SolutionUniqueName}': {lastError.Message}.";
                }
            }
            else
            {
                solutionAddNote = " WARNING: no active solution selected — the dashboard is in the default solution.";
            }

            // Publish so the dashboard is visible to users immediately. PublishAll
            // is the safe choice; the more granular PublishXmlRequest would
            // require us to construct a parameterstring with the dashboard id.
            try
            {
                await dataverseClient.PublishAllAsync(cancellationToken);
            }
            catch (Exception pubEx)
            {
                logger.LogWarning(pubEx, "PublishAll after dashboard create failed");
                solutionAddNote += $" (publish all failed: {pubEx.Message} — admin can publish manually).";
            }

            state.Dashboards.Add(new CreatedDashboard(dashboardId, dashboardName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateDashboard",
                Parameters = new { dashboardName },
                Result = new { dashboardId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteDashboard",
                    Parameters = new { dashboardId },
                    Warning = "Deleting a dashboard removes its layout permanently.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateDashboard",
                Detail = $"Created dashboard '{dashboardName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateDashboard succeeded: dashboardId={DashboardId}, name={Name}", dashboardId, dashboardName);
            return $"Dashboard '{dashboardName}' created with DashboardId={dashboardId}.{solutionAddNote}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateDashboard failed: name={Name}", dashboardName);
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateDashboard",
                Detail = $"Failed to create dashboard '{dashboardName}': {ex.Message}",
                Success = false
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            return $"ERROR: Failed to create dashboard '{dashboardName}': {ex.Message}.";
        }
    }

    /// <summary>
    /// True for the eventual-consistency 404 we see when AddSolutionComponent
    /// runs immediately after a create. We match on substring rather than the
    /// exception type because the surface we get back depends on which code
    /// path raised — sometimes it's HttpRequestException, sometimes it's the
    /// generic exception <see cref="DataverseClient"/> raises after parsing
    /// the error envelope.
    /// </summary>
    private static bool IsTransientNotFound(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("404", StringComparison.Ordinal)
            || msg.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }
}
