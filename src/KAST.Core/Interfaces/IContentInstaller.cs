using KAST.Core.Enums;
using KAST.Core.Models;

namespace KAST.Core.Interfaces;

/// <summary>
/// Each content type (local mod, Steam mod, server) implements this interface.
/// The orchestrator calls <see cref="PlanSteps"/> to build the step list, then <see cref="InstallAsync"/> to run.
/// </summary>
public interface IContentInstaller
{
    ContentType Type { get; }

    /// <summary>
    /// Returns the ordered steps this installer will execute for the given request.
    /// Called before <see cref="InstallAsync"/> so the UI can show the plan.
    /// </summary>
    IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request);

    /// <summary>
    /// Runs the install. The installer must call <see cref="ContentInstallState.BeginStep"/>,
    /// <see cref="ContentInstallState.CompleteStep"/>, etc. as it progresses.
    /// </summary>
    Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct);

    /// <summary>
    /// Post-install validation. Returns human-readable check results.
    /// </summary>
    IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request);
}

public record ContentValidationResult(string Label, bool OK, string? Detail = null);
