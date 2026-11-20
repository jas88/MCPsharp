using System.Collections.Immutable;
using System.Security.Cryptography;
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
    /// A new strong-name public key (an RSA key in the strong-name public key blob format), to public-sign an
    /// emitted assembly with
    /// </summary>
    public static ImmutableArray<byte> CreatePublicKey()
    {
        using var rsa = RSA.Create(1024);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var modulus = parameters.Modulus!.Reverse().ToArray();
        var exponent = parameters.Exponent!.Aggregate(0u, (value, b) => (value << 8) | b);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x00002400u); // signature algorithm: CALG_RSA_SIGN
        writer.Write(0x00008004u); // hash algorithm: CALG_SHA1
        writer.Write(20 + modulus.Length); // size of the key blob that follows
        writer.Write((byte)0x06); // PUBLICKEYBLOB
        writer.Write((byte)0x02); // blob version
        writer.Write((ushort)0);
        writer.Write(0x00002400u); // key algorithm: CALG_RSA_SIGN
        writer.Write(0x31415352u); // "RSA1"
        writer.Write(modulus.Length * 8); // key size in bits
        writer.Write(exponent);
        writer.Write(modulus);
        writer.Flush();
        return [.. stream.ToArray()];
    }

    /// <summary>
    /// Compile <paramref name="source"/> into the assembly <paramref name="assemblyName"/>, version
    /// <paramref name="assemblyVersion"/>, at <paramref name="path"/>, public-signed with
    /// <paramref name="publicKey"/> when given
    /// </summary>
    public static void Emit(
        string assemblyName,
        string path,
        string source,
        string assemblyVersion = "1.0.0.0",
        ImmutableArray<byte> publicKey = default)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(DiagnosticAnalyzer).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]")
            ],
            references,
            publicKey.IsDefault ? options : options.WithCryptoPublicKey(publicKey).WithPublicSign(true));

        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
