using MCPsharp.Services.Phase3.LargeFileOptimization;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Phase3;

/// <summary>
/// Table-driven tests for cyclomatic/cognitive complexity, nesting depth and decision points.
/// Cognitive expectations follow G. Ann Campbell's SonarSource "Cognitive Complexity" white paper.
/// </summary>
[TestFixture]
public class CodeComplexityCalculatorTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return Case("straight line", "void M() { var x = 1; x++; }", cyclomatic: 1, cognitive: 0, nesting: 0);

        yield return Case("white paper sumOfPrimes (labelled continue as goto)", """
            int SumOfPrimes(int max)
            {
                int total = 0;
                for (int i = 1; i <= max; ++i)      // +1
                {
                    for (int j = 2; j < i; ++j)     // +2
                    {
                        if (i % j == 0)             // +3
                        {
                            goto nextI;             // +1
                        }
                    }
                    total += i;
                    nextI: ;
                }
                return total;
            }
            """, cyclomatic: 4, cognitive: 7, nesting: 3);

        yield return Case("white paper getWords", """
            string GetWords(int number)
            {
                switch (number)                     // +1
                {
                    case 1: return "one";
                    case 2: return "a couple";
                    case 3: return "a few";
                    default: return "lots";
                }
            }
            """, cyclomatic: 4, cognitive: 1, nesting: 1);

        yield return Case("white paper logical operator sequences", """
            void M(bool a, bool b, bool c, bool d, bool e, bool f)
            {
                if (a && b && c || d || e && f) { }  // +1 if, +3 sequences
            }
            """, cyclomatic: 7, cognitive: 4, nesting: 1);

        yield return Case("white paper try/catch nesting (myMethod)", """
            void MyMethod(bool condition1, bool condition2)
            {
                try
                {
                    if (condition1)                         // +1
                    {
                        for (int i = 0; i < 10; i++)        // +2
                        {
                            while (condition2) { }          // +3
                        }
                    }
                }
                catch (System.Exception)                    // +1
                {
                    if (condition2) { }                     // +2
                }
            }
            """, cyclomatic: 6, cognitive: 9, nesting: 3);

        yield return Case("white paper lambda raises nesting (myMethod2)", """
            void MyMethod2(bool condition1)
            {
                System.Action r = () =>
                {
                    if (condition1) { }                     // +2
                };
            }
            """, cyclomatic: 2, cognitive: 2, nesting: 2);

        yield return Case("else-if chain is hybrid", """
            int M(int x)
            {
                if (x == 1) { return 1; }                   // +1
                else if (x == 2) { return 2; }              // +1
                else if (x == 3) { return 3; }              // +1
                else { return 0; }                          // +1
            }
            """, cyclomatic: 4, cognitive: 4, nesting: 1);

        yield return Case("if nested in else-if body uses chain nesting", """
            void M(bool a, bool b, bool c)
            {
                if (a) { }                                  // +1
                else if (b)                                 // +1
                {
                    if (c) { }                              // +2
                }
            }
            """, cyclomatic: 4, cognitive: 4, nesting: 2);

        yield return Case("nested conditional operators", "int M(bool a, bool b) { return a ? (b ? 1 : 2) : 3; }",
            cyclomatic: 3, cognitive: 3, nesting: 2);

        yield return Case("direct recursion", "int Fact(int n) { return n <= 1 ? 1 : n * Fact(n - 1); }",
            cyclomatic: 2, cognitive: 2, nesting: 1);

        yield return Case("overload call is not recursion", "int F(int a) => F(a, 1);",
            cyclomatic: 1, cognitive: 0, nesting: 0);

        yield return Case("switch expression with discard arm", """string S(int x) => x switch { 1 => "a", 2 => "b", _ => "c" };""",
            cyclomatic: 3, cognitive: 1, nesting: 1);

        yield return Case("null-coalescing counts for McCabe only, ?. counts nowhere", "int M(string? a) { return a?.Length ?? 0; }",
            cyclomatic: 2, cognitive: 0, nesting: 0);

        yield return Case("pattern combinators", "bool M(int x) => x is > 0 and < 10 or 42;",
            cyclomatic: 3, cognitive: 2, nesting: 0);

        yield return Case("foreach with nested do-while", """
            void M(int[] xs)
            {
                foreach (var x in xs)                       // +1
                {
                    var y = x;
                    do { y--; } while (y > 0);              // +2
                }
            }
            """, cyclomatic: 3, cognitive: 3, nesting: 2);

        yield return Case("goto case is a fundamental increment", """
            void M(int x)
            {
                switch (x)                                  // +1
                {
                    case 1: goto case 2;                    // +1
                    case 2: break;
                }
            }
            """, cyclomatic: 3, cognitive: 2, nesting: 1);

        yield return Case("recursive local function nests", """
            void M()
            {
                void Local(int n)
                {
                    if (n > 0) Local(n - 1);                // +2 if, +1 recursion
                }
                Local(3);
            }
            """, cyclomatic: 2, cognitive: 3, nesting: 2);

        yield return Case("try/finally are free", """
            void M()
            {
                try { System.Console.WriteLine(); }
                finally { System.Console.WriteLine(); }
            }
            """, cyclomatic: 1, cognitive: 0, nesting: 0);

        yield return Case("mixed operators in one condition", """
            bool M(bool a, bool b, bool c, bool d) => a && b || c && d;
            """, cyclomatic: 4, cognitive: 3, nesting: 0);
    }

    private static TestCaseData Case(string name, string member, int cyclomatic, int cognitive, int nesting) =>
        new TestCaseData(member, cyclomatic, cognitive, nesting).SetName($"Complexity: {name}");

    [TestCaseSource(nameof(Cases))]
    public void Analyze_ReturnsExpectedMetrics(string member, int cyclomatic, int cognitive, int nesting)
    {
        var result = AnalyzeSingle(member);

        Assert.Multiple(() =>
        {
            Assert.That(result.CyclomaticComplexity, Is.EqualTo(cyclomatic), "cyclomatic");
            Assert.That(result.CognitiveComplexity, Is.EqualTo(cognitive), "cognitive");
            Assert.That(result.MaxNestingDepth, Is.EqualTo(nesting), "nesting");
            Assert.That(result.DecisionPoints, Is.EqualTo(cyclomatic - 1), "decision points");
            Assert.That(result.Increments.Sum(i => i.Amount), Is.EqualTo(cognitive), "increments sum");
        });
    }

    [Test]
    public void Analyze_SumOfPrimes_RecordsIncrementsWithNesting()
    {
        var result = AnalyzeSingle("""
            int SumOfPrimes(int max)
            {
                int total = 0;
                for (int i = 1; i <= max; ++i)
                {
                    for (int j = 2; j < i; ++j)
                    {
                        if (i % j == 0) { goto nextI; }
                    }
                    total += i;
                    nextI: ;
                }
                return total;
            }
            """);

        var increments = result.Increments.Select(i => (i.Construct, i.Amount, i.Nesting)).ToList();
        Assert.That(increments, Is.EqualTo(new[]
        {
            ("for", 1, 0),
            ("for", 2, 1),
            ("if", 3, 2),
            ("goto", 1, 3)
        }));
    }

    [Test]
    public void Analyze_ReportsTopLevelBlocksWithTheirCost()
    {
        var result = AnalyzeSingle("""
            void M(bool a, bool b)
            {
                if (a)
                {
                    while (b) { }
                }
                for (;;) { }
            }
            """);

        Assert.That(result.Blocks.Select(b => (b.Construct, b.CognitiveComplexity)), Is.EqualTo(new[]
        {
            ("if", 3),
            ("for", 1)
        }));
    }

    [Test]
    public void AnalyzeMembers_MeasuresAllFunctionLikeMembersWithoutDoubleCountingLambdas()
    {
        var root = CSharpSyntaxTree.ParseText("""
            class C
            {
                private int _x;
                C(int x) { if (x > 0) _x = x; }
                int Value { get { return _x > 0 ? _x : 0; } set { _x = value; } }
                int Doubled => _x * 2;
                abstract class Nested { public abstract void NoBody(); }
                void Run() { System.Func<int, int> f = y => y > 0 ? y : -y; }
                public static C operator +(C a, C b) => a;
            }
            """).GetRoot();

        var members = CodeComplexityCalculator.AnalyzeMembers(root);

        Assert.That(members.Select(m => (m.Name, m.Kind, m.CyclomaticComplexity)), Is.EqualTo(new[]
        {
            ("C", "Constructor", 2),
            ("Value.get", "Accessor", 2),
            ("Value.set", "Accessor", 1),
            ("Doubled.get", "Accessor", 1),
            ("Run", "Method", 2),
            ("operator +", "Operator", 1)
        }));
    }

    private static FunctionComplexity AnalyzeSingle(string member)
    {
        var root = CSharpSyntaxTree.ParseText("class C {\n" + member + "\n}").GetRoot();
        var diagnostics = root.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.That(diagnostics, Is.Empty, "test snippet must parse");
        return CodeComplexityCalculator.AnalyzeMembers(root).Single();
    }
}
