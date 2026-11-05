using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Agent.Core.Workspace;

/// <summary>
/// Applies search-and-replace edits (FR-3.2). The search text must match exactly once,
/// so an edit cannot silently change the wrong place.
/// </summary>
public static class PatchApplier
{
    public static string ApplySingle(string original, string search, string replace)
    {
        ArgumentException.ThrowIfNullOrEmpty(search);
        ArgumentNullException.ThrowIfNull(replace);

        var first = original.IndexOf(search, StringComparison.Ordinal);
        if (first < 0)
        {
            throw new PatchException("The search text was not found in the file.");
        }

        if (original.IndexOf(search, first + search.Length, StringComparison.Ordinal) >= 0)
        {
            throw new PatchException("The search text matches more than once. Include more surrounding lines.");
        }

        return original[..first] + replace + original[(first + search.Length)..];
    }

    /// <summary>Builds a unified-style diff of the change, for review before or after it is applied.</summary>
    public static string UnifiedDiff(string path, string before, string after)
    {
        var model = new InlineDiffBuilder(new Differ()).BuildDiffModel(before, after);

        var lines = new List<string>
        {
            $"--- a/{path}",
            $"+++ b/{path}",
        };

        foreach (var line in model.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    lines.Add("+" + line.Text);
                    break;
                case ChangeType.Deleted:
                    lines.Add("-" + line.Text);
                    break;
                case ChangeType.Modified:
                    lines.Add("-" + line.Text);
                    break;
                case ChangeType.Unchanged:
                    lines.Add(" " + line.Text);
                    break;
                case ChangeType.Imaginary:
                    break;
            }
        }

        return string.Join('\n', lines);
    }
}

/// <summary>Raised when an edit cannot be applied safely.</summary>
public sealed class PatchException(string message) : InvalidOperationException(message);
