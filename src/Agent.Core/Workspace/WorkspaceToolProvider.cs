using Agent.Core.ToolForge;
using Agent.Core.Workflows;

namespace Agent.Core.Workspace;

/// <summary>
/// The workspace as tools: read a file, search files, and propose an edit. Reads run at once. An edit is a write, so the
/// agent holds it for approval before it reaches this provider (FR-3.1 to FR-3.5).
/// </summary>
public sealed class WorkspaceToolProvider(WorkspaceService workspace) : IToolProvider
{
    public const string ReadName = "workspace_read";
    public const string SearchName = "workspace_search";
    public const string EditName = "workspace_edit";
    public const string CreateName = "workspace_create";
    public const string ListName = "workspace_list";

    private const int MaxOutputCharacters = 8000;
    private const int MaxSearchHits = 20;

    private static readonly IReadOnlyList<ToolDescriptor> Tools =
    [
        new(ListName, SideEffect.ReadOnly, [], "List the text files in the workspace, with their relative paths. Use it before reading or editing a file you have not seen."),
        new(ReadName, SideEffect.ReadOnly, ["path"], "Read a text file in the workspace. Give its relative path."),
        new(SearchName, SideEffect.ReadOnly, ["query"], "Find lines that contain the text in workspace files. Returns file, line, and text."),
        new(EditName, SideEffect.Write, ["path", "search", "replace"], "Replace one exact, unique piece of text in a workspace file. Requires approval."),
        new(CreateName, SideEffect.Write, ["path", "content"], "Create a new text file in the workspace. Never overwrites a file. Requires approval."),
    ];

    public ToolDescriptor? Find(string name) => Tools.FirstOrDefault(tool => tool.Name == name);

    public IReadOnlyList<ToolDescriptor> List() => Tools;

    public async Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
    {
        switch (tool)
        {
            case ListName:
                {
                    var files = await workspace.ListFilesAsync(cancellationToken).ConfigureAwait(false);
                    return files.Count == 0 ? "The workspace has no text files." : Truncate(string.Join('\n', files));
                }

            case ReadName:
                return Truncate(await workspace.ReadAsync(Required(inputs, "path"), cancellationToken).ConfigureAwait(false));

            case SearchName:
                {
                    var hits = await workspace.SearchAsync(Required(inputs, "query"), MaxSearchHits, cancellationToken).ConfigureAwait(false);
                    return hits.Count == 0
                        ? "No matches."
                        : Truncate(string.Join('\n', hits.Select(hit => $"{hit.Path}:{hit.Line}: {hit.Text}")));
                }

            case EditName:
                {
                    var result = await workspace.EditAsync(
                            Required(inputs, "path"),
                            Required(inputs, "search"),
                            inputs.TryGetValue("replace", out var replace) ? replace : string.Empty,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Truncate(result.Diff);
                }

            case CreateName:
                {
                    var created = await workspace.CreateAsync(
                            Required(inputs, "path"),
                            inputs.TryGetValue("content", out var content) ? content : string.Empty,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return "Created " + created + ".";
                }

            default:
                throw new InvalidOperationException("The tool is not available.");
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> inputs, string key) =>
        inputs.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : throw new ArgumentException($"Missing input: {key}.");

    private static string Truncate(string text) =>
        text.Length <= MaxOutputCharacters ? text : text[..MaxOutputCharacters] + "\n[truncated]";
}
