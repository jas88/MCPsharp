using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Compiles analyzers into assemblies on disk, for tests that load analyzer assemblies the way MCPsharp does
/// </summary>
internal static class AnalyzerAssemblyEmitter
{
    /// <summary>
    /// Source of a C# analyzer class named <paramref name="className"/>, in the global namespace, that supports
    /// <paramref name="diagnosticId"/> and reports nothing
    /// </summary>
    public static string AnalyzerSource(string className, string diagnosticId) => $$"""
        using System.Collections.Immutable;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.Diagnostics;

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        public sealed class {{className}} : DiagnosticAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = new(
                "{{diagnosticId}}", "Probe", "Probe", "Testing", DiagnosticSeverity.Info, isEnabledByDefault: true);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

            public override void Initialize(AnalysisContext context)
            {
                context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                context.EnableConcurrentExecution();
            }
        }
        """;

    /// <summary>
    /// Source of a C# analyzer class named <paramref name="className"/>, in the global namespace, that reports
    /// <paramref name="diagnosticId"/> on the name of every class declared
    /// </summary>
    public static string ReportingAnalyzerSource(string className, string diagnosticId) => $$"""
        using System.Collections.Immutable;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Microsoft.CodeAnalysis.Diagnostics;

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        public sealed class {{className}} : DiagnosticAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = new(
                "{{diagnosticId}}", "Probe", "Class {0}", "Testing", DiagnosticSeverity.Warning, isEnabledByDefault: true);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

            public override void Initialize(AnalysisContext context)
            {
                context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                context.EnableConcurrentExecution();
                context.RegisterSyntaxNodeAction(
                    c =>
                    {
                        var identifier = ((ClassDeclarationSyntax)c.Node).Identifier;
                        c.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.Text));
                    },
                    SyntaxKind.ClassDeclaration);
            }
        }
        """;

    /// <summary>
    /// Compile <paramref name="source"/> into the assembly <paramref name="assemblyName"/>, version
    /// <paramref name="assemblyVersion"/>, at <paramref name="path"/>
    /// </summary>
    public static void Emit(string assemblyName, string path, string source, string assemblyVersion = "1.0.0.0")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(DiagnosticAnalyzer).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
