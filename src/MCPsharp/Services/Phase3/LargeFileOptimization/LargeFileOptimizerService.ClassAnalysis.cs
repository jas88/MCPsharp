using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Class and method metrics and god-class detection for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    #region Class Analysis Methods

    private static ClassMetrics AnalyzeClassMetrics(ClassDeclarationSyntax classDecl, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var lineCount = classDecl.GetText().Lines.Count;
        var methodDeclarations = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var propertyDeclarations = classDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();
        var fieldDeclarations = classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();
        var constructorDeclarations = classDecl.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToList();

        // Calculate complexity
        var complexity = CalculateClassComplexity(classDecl, cancellationToken);

        // Analyze responsibilities
        var responsibilities = IdentifyClassResponsibilities(classDecl);

        // Analyze dependencies
        var dependencies = IdentifyClassDependencies(classDecl, semanticModel);

        // Analyze God class score
        var godClassScore = AnalyzeGodClass(classDecl, semanticModel, classDecl.SyntaxTree.FilePath);

        return new ClassMetrics
        {
            LineCount = lineCount,
            MethodCount = methodDeclarations.Count,
            PropertyCount = propertyDeclarations.Count,
            FieldCount = fieldDeclarations.Count,
            ConstructorCount = constructorDeclarations.Count,
            Complexity = complexity,
            Responsibilities = responsibilities,
            Dependencies = dependencies,
            GodClassScore = godClassScore,
            IsTooLarge = lineCount > 300 || methodDeclarations.Count > 20,
            HasTooManyResponsibilities = responsibilities.Count > 5
        };
    }

    private static List<string> IdentifyClassResponsibilities(ClassDeclarationSyntax classDecl)
    {
        var methodNames = classDecl.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => m.Identifier.Text)
            .ToList();

        // Group methods by naming patterns to identify responsibilities
        var responsibilitiesByPattern = new Dictionary<string, List<string>>();

        foreach (var methodName in methodNames)
        {
            var pattern = IdentifyResponsibilityPattern(methodName);
            if (!responsibilitiesByPattern.TryGetValue(pattern, out var value))
            {
                value = new List<string>();
                responsibilitiesByPattern[pattern] = value;
            }

            value.Add(methodName);
        }

        return responsibilitiesByPattern.Keys.ToList();
    }

    private static string IdentifyResponsibilityPattern(string methodName)
    {
        if (methodName.StartsWith("Get", StringComparison.Ordinal) || methodName.StartsWith("Set", StringComparison.Ordinal))
            return "Data Access";
        if (methodName.StartsWith("Validate", StringComparison.Ordinal) || methodName.StartsWith("Check", StringComparison.Ordinal))
            return "Validation";
        if (methodName.StartsWith("Calculate", StringComparison.Ordinal) || methodName.StartsWith("Compute", StringComparison.Ordinal))
            return "Calculation";
        if (methodName.StartsWith("Save", StringComparison.Ordinal) || methodName.StartsWith("Load", StringComparison.Ordinal) || methodName.StartsWith("Delete", StringComparison.Ordinal))
            return "Persistence";
        if (methodName.StartsWith("Render", StringComparison.Ordinal) || methodName.StartsWith("Draw", StringComparison.Ordinal))
            return "Rendering";
        if (methodName.StartsWith("Send", StringComparison.Ordinal) || methodName.StartsWith("Receive", StringComparison.Ordinal))
            return "Communication";
        if (methodName.StartsWith("Parse", StringComparison.Ordinal) || methodName.StartsWith("Format", StringComparison.Ordinal))
            return "Data Processing";

        return "Other";
    }

    private static List<string> IdentifyClassDependencies(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
    {
        var dependencies = new HashSet<string>();
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);

        if (classSymbol != null)
        {
            // Get base types
            if (classSymbol.BaseType != null)
            {
                dependencies.Add(classSymbol.BaseType.ToDisplayString());
            }

            // Get interfaces
            foreach (var iface in classSymbol.AllInterfaces)
            {
                dependencies.Add(iface.ToDisplayString());
            }

            // Get field and property types
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IFieldSymbol field)
                {
                    dependencies.Add(field.Type.ToDisplayString());
                }
                else if (member is IPropertySymbol property)
                {
                    dependencies.Add(property.Type.ToDisplayString());
                }
            }
        }

        return dependencies.ToList();
    }

    #endregion

    #region Method Analysis Methods

    private static MethodMetrics AnalyzeMethodMetrics(MethodDeclarationSyntax methodDecl)
    {
        var lineCount = methodDecl.GetText().Lines.Count;
        var parameterCount = methodDecl.ParameterList.Parameters.Count;

        // Count local variables
        var localVariableCount = 0;
        // Count control structures
        var loopCount = 0;
        var conditionalCount = 0;
        var tryCatchCount = 0;

        if (methodDecl.Body != null)
        {
            var localDeclarations = methodDecl.Body.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>();
            localVariableCount = localDeclarations.Count();

            loopCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is ForStatementSyntax || n is WhileStatementSyntax || n is ForEachStatementSyntax);

            conditionalCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is IfStatementSyntax || n is SwitchStatementSyntax);

            tryCatchCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is TryStatementSyntax);
        }

        // Calculate complexity
        var complexity = CalculateMethodComplexity(methodDecl);

        // Analyze God method score
        var godMethodScore = AnalyzeGodMethod(methodDecl, methodDecl.SyntaxTree.FilePath);

        return new MethodMetrics
        {
            LineCount = lineCount,
            ParameterCount = parameterCount,
            LocalVariableCount = localVariableCount,
            LoopCount = loopCount,
            ConditionalCount = conditionalCount,
            TryCatchCount = tryCatchCount,
            MaximumNestingDepth = complexity.MaximumNestingDepth,
            Complexity = complexity,
            GodMethodScore = godMethodScore,
            IsTooLarge = lineCount > GodMethodThreshold,
            HasTooManyParameters = parameterCount > MaxParameters,
            IsTooComplex = IsTooComplex(complexity)
        };
    }

    #endregion

    #region God Class Analysis

    private static GodClassAnalysis AnalyzeGodClass(ClassDeclarationSyntax classDecl, SemanticModel semanticModel, string filePath)
    {
        var className = classDecl.Identifier.Text;
        var violations = new List<string>();
        var responsibilities = IdentifyClassResponsibilities(classDecl);
        var methods = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var fields = classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();

        // Calculate God class score
        var godClassScore = CalculateGodClassScore(classDecl, methods, fields, responsibilities);

        // Identify violations
        if (methods.Count > GodClassThreshold)
            violations.Add($"Too many methods: {methods.Count} (threshold: {GodClassThreshold})");

        if (fields.Count > 20)
            violations.Add($"Too many fields: {fields.Count} (threshold: 20)");

        if (responsibilities.Count > 5)
            violations.Add($"Too many responsibilities: {responsibilities.Count} (threshold: 5)");

        // Find problematic methods
        var tooManyMethods = methods
            .Where(m => m.GetText().Lines.Count > 50)
            .Select(m => $"{m.Identifier.Text} ({m.GetText().Lines.Count} lines)")
            .ToList();

        var tooManyFields = fields
            .Select(f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text)
            .Where(n => !string.IsNullOrEmpty(n))
            .OfType<string>()
            .ToList();

        // Analyze coupling
        var highCouplingClasses = IdentifyHighCouplingClasses(classDecl, semanticModel);

        // Generate recommended splits
        var recommendedSplits = GenerateClassSplittingStrategies(classDecl, responsibilities);

        var severity = DetermineGodClassSeverity(godClassScore);

        return new GodClassAnalysis
        {
            ClassName = className,
            FilePath = filePath,
            GodClassScore = godClassScore,
            Severity = severity,
            Violations = violations,
            Responsibilities = responsibilities,
            TooManyMethods = tooManyMethods,
            TooManyFields = tooManyFields,
            HighCouplingClasses = highCouplingClasses,
            RecommendedSplits = recommendedSplits
        };
    }

    private static double CalculateGodClassScore(ClassDeclarationSyntax classDecl, List<MethodDeclarationSyntax> methods, List<FieldDeclarationSyntax> fields, List<string> responsibilities)
    {
        var methodScore = Math.Min(1.0, methods.Count / (double)GodClassThreshold);
        var fieldScore = Math.Min(1.0, fields.Count / 20.0);
        var responsibilityScore = Math.Min(1.0, responsibilities.Count / 5.0);
        var lineCountScore = Math.Min(1.0, classDecl.GetText().Lines.Count / 500.0);

        return (methodScore + fieldScore + responsibilityScore + lineCountScore) / 4.0;
    }

    private static GodClassSeverity DetermineGodClassSeverity(double score)
    {
        if (score <= 0.3)
            return GodClassSeverity.None;
        if (score <= 0.5)
            return GodClassSeverity.Low;
        if (score <= 0.7)
            return GodClassSeverity.Medium;
        if (score <= 0.9)
            return GodClassSeverity.High;
        return GodClassSeverity.Critical;
    }

    private static List<string> IdentifyHighCouplingClasses(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
    {
        var highCouplingClasses = new List<string>();
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);

        if (classSymbol != null)
        {
            // This is a simplified analysis
            // In practice, you'd analyze method calls, field usage, etc.
        }

        return highCouplingClasses;
    }

    #endregion
}
