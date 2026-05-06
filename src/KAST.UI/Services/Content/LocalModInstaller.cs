using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.UI.Services.Content;

/// <summary>
/// Installs a local mod: extracts a ZIP or validates an existing folder.
/// </summary>
public class LocalModInstaller(IFileSystemService fs) : IContentInstaller
{
    public ContentType Type => ContentType.LocalMod;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request)
    {
        bool isZip = request.SourcePath?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
        var steps = new List<ContentStep>();

        if (isZip)
            steps.Add(new ContentStep { Name = "Extract archive", Detail = Path.GetFileName(request.SourcePath) });

        steps.Add(new ContentStep { Name = "Calculate size", Detail = request.DestinationPath });

        return steps;
    }

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        bool isZip = request.SourcePath?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
        int stepIdx = 0;

        if (isZip)
        {
            state.BeginStep(stepIdx);
            state.AddLog($"Extracting {Path.GetFileName(request.SourcePath)} → {request.DestinationPath}");

            var progress = new Progress<double>(pct => state.SetStepProgress(stepIdx, pct));
            await fs.ExtractZipAsync(request.SourcePath!, request.DestinationPath, progress, ct);

            state.CompleteStep(stepIdx);
            state.AddLog("Extraction complete.");
            stepIdx++;
        }

        state.BeginStep(stepIdx);
        state.AddLog("Calculating size on disk...");

        long size = fs.GetDirectorySize(request.DestinationPath);
        state.AddLog($"Size: {size / (1024.0 * 1024.0):F1} MiB");

        state.CompleteStep(stepIdx);
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        bool exists = Directory.Exists(request.DestinationPath);
        results.Add(new("Mod directory", exists, request.DestinationPath));

        if (exists)
        {
            long size = fs.GetDirectorySize(request.DestinationPath);
            results.Add(new("Files on disk", size > 0, $"{size / (1024.0 * 1024.0):F1} MiB"));
        }

        return results;
    }
}
