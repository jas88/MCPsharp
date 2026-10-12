using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// Generates and restores a small three-project solution in a temporary directory:
/// <list type="bullet">
/// <item><c>Lib</c> - multi-targeted (netstandard2.0 + current TFM) class library declaring
/// <c>IGreeter</c>, <c>Greeter</c> and the abstract <c>Shape</c></item>
/// <item><c>App</c> - console app referencing Lib (calls <c>Greeter.Greet</c>, derives <c>Circle : Shape</c>)</item>
/// <item><c>Extras</c> - class library referencing Lib (calls <c>Greeter.Format</c>, derives <c>Square : Shape</c>)</item>
/// </list>
/// MSBuildWorkspace needs restored projects, so <c>dotnet restore</c> is run on creation.
/// </summary>
public sealed class MultiProjectSolutionFixture : IDisposable
{
    private MultiProjectSolutionFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string SolutionPath => Path.Combine(Root, "Multi.sln");

    public string SlnxPath => Path.Combine(Root, "Multi.slnx");

    public string LibGreeterPath => Path.Combine(Root, "Lib", "Greeter.cs");

    public string LibShapePath => Path.Combine(Root, "Lib", "Shape.cs");

    public string AppProgramPath => Path.Combine(Root, "App", "Program.cs");

    public string ExtrasSquarePath => Path.Combine(Root, "Extras", "Square.cs");

    /// <summary>
    /// Current runtime's target framework moniker (e.g. net10.0), always installed with the SDK running the tests.
    /// </summary>
    private static string CurrentTfm => $"net{Environment.Version.Major}.0";

    public static async Task<MultiProjectSolutionFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "MCPsharp_MultiProject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = new MultiProjectSolutionFixture(root);

        try
        {
            await fixture.WriteFilesAsync();
            await RestoreAsync(fixture.SolutionPath, root);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private async Task WriteFilesAsync()
    {
        // Isolate the generated solution from any Directory.Build.* / central package management above the temp dir
        await WriteAsync("Directory.Build.props", "<Project />");
        await WriteAsync("Directory.Build.targets", "<Project />");
        await WriteAsync("Directory.Packages.props", "<Project />");

        await WriteAsync(Path.Combine("Lib", "Lib.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>netstandard2.0;{CurrentTfm}</TargetFrameworks>
                <LangVersion>latest</LangVersion>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        await WriteAsync(Path.Combine("Lib", "Greeter.cs"), """
            namespace Lib
            {
                public interface IGreeter
                {
                    string Greet(string name);
                }

                public class Greeter : IGreeter
                {
                    public string Greet(string name)
                    {
                        return Format(name);
                    }

                    public static string Format(string name)
                    {
                        return "Hello " + name;
                    }
                }
            }
            """);

        await WriteAsync(Path.Combine("Lib", "Shape.cs"), """
            namespace Lib
            {
                public abstract class Shape
                {
                    public abstract double Area();
                }
            }
            """);

        await WriteAsync(Path.Combine("App", "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>{CurrentTfm}</TargetFramework>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        await WriteAsync(Path.Combine("App", "Program.cs"), """
            using Lib;

            namespace App
            {
                public static class Program
                {
                    public static void Main()
                    {
                        var greeter = new Greeter();
                        System.Console.WriteLine(greeter.Greet("world"));
                        IGreeter abstraction = greeter;
                        System.Console.WriteLine(abstraction.Greet("again"));
                        Shape shape = new Circle(2);
                        System.Console.WriteLine(shape.Area());
                    }
                }

                public class Circle : Shape
                {
                    private readonly double _radius;

                    public Circle(double radius)
                    {
                        _radius = radius;
                    }

                    public override double Area()
                    {
                        return 3.14 * _radius * _radius;
                    }
                }
            }
            """);

        await WriteAsync(Path.Combine("Extras", "Extras.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{CurrentTfm}</TargetFramework>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        await WriteAsync(Path.Combine("Extras", "Square.cs"), """
            using Lib;

            namespace Extras
            {
                public class Square : Shape
                {
                    private readonly double _side;

                    public Square(double side)
                    {
                        _side = side;
                    }

                    public override double Area()
                    {
                        return _side * _side;
                    }

                    public string Describe()
                    {
                        return Greeter.Format("square");
                    }
                }
            }
            """);

        await WriteAsync("Multi.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Lib", "Lib\Lib.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "App", "App\App.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Extras", "Extras\Extras.csproj", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{33333333-3333-3333-3333-333333333333}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{33333333-3333-3333-3333-333333333333}.Debug|Any CPU.Build.0 = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """);

        await WriteAsync("Multi.slnx", """
            <Solution>
              <Project Path="Lib/Lib.csproj" />
              <Project Path="App/App.csproj" />
              <Project Path="Extras/Extras.csproj" />
            </Solution>
            """);
    }

    private Task WriteAsync(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, content);
    }

    private static async Task RestoreAsync(string solutionPath, string workingDirectory)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet) || !File.Exists(dotnet))
        {
            dotnet = "dotnet";
        }

        var startInfo = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("-nodeReuse:false");
        // Restoring the solution's projects in parallel can race on a shared project's
        // obj/project.assets.json ("already exists") under load
        startInfo.ArgumentList.Add("--disable-parallel");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start 'dotnet restore'");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet restore' failed with exit code {process.ExitCode}:\n{await stdout}\n{await stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp directory
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp directory
        }
    }
}
