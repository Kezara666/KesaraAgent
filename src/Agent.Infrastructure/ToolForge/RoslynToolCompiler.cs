using Agent.Core.ToolForge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Agent.Infrastructure.ToolForge;

/// <summary>
/// Compiles generated code in memory with Roslyn. Nothing is executed and nothing is written to disk.
/// References are limited to the shared framework, so a tool cannot pull in extra assemblies.
/// </summary>
public sealed class RoslynToolCompiler : IToolCompiler
{
    private static readonly Lazy<List<MetadataReference>> FrameworkReferences = new(LoadFrameworkReferences);

    public CompileResult Compile(GeneratedTool tool)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(tool.Source);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"generated_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: FrameworkReferences.Value,
            options: options);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        if (emit.Success)
        {
            return new CompileResult(true, []);
        }

        var errors = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();

        return new CompileResult(false, errors);
    }

    private static List<MetadataReference> LoadFrameworkReferences()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("System.Private.CoreLib.dll", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("netstandard.dll", StringComparison.Ordinal))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
