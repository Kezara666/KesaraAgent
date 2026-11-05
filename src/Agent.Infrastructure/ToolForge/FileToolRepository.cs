using System.Text.Json;
using Agent.Core.ToolForge;

namespace Agent.Infrastructure.ToolForge;

/// <summary>
/// Stages generated tools under <c>tools/staging</c> and promotes them to <c>tools/generated/&lt;domain&gt;/&lt;name&gt;</c>
/// with a manifest. Paths are built only from validated names, and the resolved path must stay inside the root.
/// </summary>
public sealed class FileToolRepository(string toolsRoot) : IToolRepository
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    private readonly string root = Path.GetFullPath(toolsRoot);

    public async Task StageAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken)
    {
        spec.Validate();
        var folder = Path.Combine(root, "staging", spec.Name);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{spec.Name}.cs"), tool.Source, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{spec.Name}.Tests.cs"), tool.TestSource, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> PromoteAsync(ToolSpec spec, string reason, CancellationToken cancellationToken)
    {
        spec.Validate();
        var staging = Path.Combine(root, "staging", spec.Name);
        if (!Directory.Exists(staging))
        {
            throw new DirectoryNotFoundException("The tool is not staged.");
        }

        var target = Path.Combine(root, "generated", spec.Domain, spec.Name);
        EnsureInside(root, target);
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(staging))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        var manifest = new
        {
            name = spec.Name,
            version = "1.0.0",
            domain = spec.Domain,
            sideEffect = spec.SideEffect.ToString(),
            needsNetwork = spec.NeedsNetwork,
            purpose = spec.Purpose,
            reason,
            promotedAt = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
                Path.Combine(target, "manifest.json"),
                JsonSerializer.Serialize(manifest, ManifestOptions),
                cancellationToken)
            .ConfigureAwait(false);

        Directory.Delete(staging, recursive: true);
        return Path.GetRelativePath(root, target).Replace('\\', '/');
    }

    private static void EnsureInside(string root, string candidate)
    {
        var fullRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The tool path resolves outside the tools folder.");
        }
    }
}
