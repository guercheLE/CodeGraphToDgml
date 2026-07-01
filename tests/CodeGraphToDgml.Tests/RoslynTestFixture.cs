using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeGraphToDgml.Tests;

/// <summary>
/// Builds a minimal in-memory Roslyn <see cref="Solution"/> from a C# source string, so
/// Roslyn-backed logic (<c>CodeGraphToDgml.Roslyn</c>) can be exercised against a real
/// <see cref="SemanticModel"/> without needing a VS SDK host.
/// </summary>
internal static class RoslynTestFixture
{
    private static readonly Lazy<MetadataReference[]> SystemReferences = new(() =>
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpa))
        {
            // Fallback for hosts that don't expose TPA (shouldn't happen on net10.0 test host).
            return new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        }

        return tpa!
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    });

    public static (Solution Solution, DocumentId DocumentId) CreateSolution(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestAssembly",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddMetadataReferences(projectId, SystemReferences.Value);

        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Test.cs", SourceText.From(source));

        return (solution, documentId);
    }

    public static async Task<IMethodSymbol> GetMethodSymbolAsync(Solution solution, string typeName, string methodName)
    {
        var project = solution.Projects.Single();
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
        var type = compilation!.Assembly.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' not found.");

        return type.GetMembers(methodName).OfType<IMethodSymbol>().First();
    }

    public static async Task<IReadOnlyList<CalleeSymbolInfo>> GetCalleesAsync(Solution solution, string typeName, string methodName)
    {
        var method = await GetMethodSymbolAsync(solution, typeName, methodName).ConfigureAwait(false);
        var callees = await CallGraphSyntaxWalker.FindCalleesAsync(method, solution, default).ConfigureAwait(false);
        return callees.Select(c => new CalleeSymbolInfo(c.Symbol.Name, c.Symbol.ContainingType?.Name, c.FluentReceiver?.Name)).ToList();
    }

    /// <summary>
    /// Compiles <paramref name="externalSource"/> to an in-memory assembly and references it as
    /// metadata (not source) from a second project containing <paramref name="mainSource"/>. This
    /// makes types declared in <paramref name="externalSource"/> genuinely "external" — i.e.
    /// <c>symbol.Locations.Any(l =&gt; l.IsInSource)</c> is false for them within the main project's
    /// compilation — mirroring how a NuGet-referenced base class (e.g. ASP.NET Core's
    /// ControllerBase) looks from the analyzed solution's point of view.
    /// </summary>
    public static async Task<Solution> CreateSolutionWithExternalLibraryAsync(string externalSource, string mainSource)
    {
        var externalTree = CSharpSyntaxTree.ParseText(externalSource, new CSharpParseOptions(LanguageVersion.Latest));
        var externalCompilation = CSharpCompilation.Create(
            "ExternalLib",
            [externalTree],
            SystemReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = externalCompilation.Emit(peStream);
        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Failed to compile external test library:\n{errors}");
        }

        peStream.Position = 0;
        var externalReference = MetadataReference.CreateFromStream(peStream);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestAssembly",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddMetadataReferences(projectId, SystemReferences.Value)
            .AddMetadataReference(projectId, externalReference);

        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Test.cs", SourceText.From(mainSource));

        return await Task.FromResult(solution);
    }
}

internal sealed record CalleeSymbolInfo(string Name, string? ContainingTypeName, string? FluentReceiverName);
