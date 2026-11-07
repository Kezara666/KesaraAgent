using System.Text.Json;

namespace Agent.Infrastructure.ToolForge;

/// <summary>A tool that was generated and promoted. Promoted tools are recorded here, but they are not run by the agent.</summary>
public sealed record PromotedTool(string Name, string Domain, string SideEffect, string Purpose, DateTimeOffset PromotedAt);

/// <summary>
/// Reads the manifests of promoted tools under <c>tools/generated</c>. Promotion records a tool, so the tool browser can
/// show it. Generated code is never loaded or referenced by this process (CLAUDE.md rules).
/// </summary>
public sealed class PromotedToolIndex(string toolsRoot)
{
    private readonly string generated = Path.Combine(Path.GetFullPath(toolsRoot), "generated");

    public Task<IReadOnlyList<PromotedTool>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var tools = new List<PromotedTool>();
        if (!Directory.Exists(generated))
        {
            return Task.FromResult<IReadOnlyList<PromotedTool>>(tools);
        }

        foreach (var manifestPath in Directory.EnumerateFiles(generated, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                tools.Add(new PromotedTool(
                    root.GetProperty("name").GetString() ?? string.Empty,
                    root.GetProperty("domain").GetString() ?? string.Empty,
                    root.GetProperty("sideEffect").GetString() ?? string.Empty,
                    root.GetProperty("purpose").GetString() ?? string.Empty,
                    root.GetProperty("promotedAt").GetDateTimeOffset()));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
            {
                // A damaged manifest is skipped, so one bad file never hides the others.
            }

            if (tools.Count >= limit)
            {
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<PromotedTool>>(tools.OrderByDescending(tool => tool.PromotedAt).Take(limit).ToList());
    }
}
