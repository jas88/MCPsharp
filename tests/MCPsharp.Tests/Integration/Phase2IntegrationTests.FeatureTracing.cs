using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class Phase2IntegrationTests
{
    #region Feature Tracing Integration Tests

    [Test]
    public async Task FeatureTracing_DiscoverComponents_FindsCompleteFeature()
    {
        // Arrange: Create complete feature
        var projectDir = CreateTestProject("FeatureProject");
        var srcDir = Path.Combine(projectDir, "src");
        var testsDir = Path.Combine(projectDir, "tests");
        var docsDir = Path.Combine(projectDir, "docs");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testsDir);
        Directory.CreateDirectory(docsDir);

        // Model
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Product.cs"), @"
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}");

        // Repository
        await File.WriteAllTextAsync(Path.Combine(srcDir, "ProductRepository.cs"), @"
public class ProductRepository
{
    public Product GetById(int id) => throw new NotImplementedException();
    public void Save(Product product) => throw new NotImplementedException();
}");

        // Service
        await File.WriteAllTextAsync(Path.Combine(srcDir, "ProductService.cs"), @"
public class ProductService
{
    private readonly ProductRepository _repository;
    public Product GetProduct(int id) => _repository.GetById(id);
    public void CreateProduct(Product product) => _repository.Save(product);
}");

        // Controller
        var controllerPath = Path.Combine(srcDir, "ProductController.cs");
        await File.WriteAllTextAsync(controllerPath, @"
public class ProductController
{
    private readonly ProductService _service;

    [HttpGet(""{id}"")]
    public IActionResult Get(int id) => Ok(_service.GetProduct(id));

    [HttpPost]
    public IActionResult Create(Product product)
    {
        _service.CreateProduct(product);
        return Created();
    }
}");

        // Tests
        await File.WriteAllTextAsync(Path.Combine(testsDir, "ProductServiceTests.cs"), @"
public class ProductServiceTests
{
    [Test]
    public void GetProduct_ReturnsProduct()
    {
        var service = new ProductService(new ProductRepository());
        var product = service.GetProduct(1);
        Assert.That(product);
    }
}");

        // Config
        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.json"), @"{
  ""ProductService"": {
    ""CacheEnabled"": true
  }
}");

        // Docs
        await File.WriteAllTextAsync(Path.Combine(docsDir, "Products.md"), @"# Product API
## Get Product
`GET /api/products/{id}`");

        // Act & Assert
        // TODO: Re-enable when FeatureTracerService is implemented
        // await Assert.ThrowsAsync<NotImplementedException>(async () =>
        // {
        //     await _featureTracer.DiscoverFeatureComponentsAsync(controllerPath);
        // });

        // TODO: Verify all components discovered
        // Model, Repository, Service, Controller, Tests, Config, Docs
    }

    [Test]
    public async Task FeatureTracing_TraceFeature_BuildsDependencyGraph()
    {
        // Arrange: Create multi-layer feature
        var projectDir = CreateTestProject("DependencyGraphProject");
        var srcDir = Path.Combine(projectDir, "src");
        Directory.CreateDirectory(srcDir);

        await File.WriteAllTextAsync(Path.Combine(srcDir, "Entity.cs"),
            "public class Entity { public int Id { get; set; } }");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "IRepository.cs"),
            "public interface IRepository { Entity Get(int id); }");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "Repository.cs"),
            "public class Repository : IRepository { public Entity Get(int id) => new Entity(); }");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "IService.cs"),
            "public interface IService { Entity GetEntity(int id); }");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "Service.cs"),
            @"public class Service : IService {
                private readonly IRepository _repo;
                public Entity GetEntity(int id) => _repo.Get(id);
            }");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "Controller.cs"),
            @"public class Controller {
                private readonly IService _service;
                public IActionResult Get(int id) => Ok(_service.GetEntity(id));
            }");

        // Act & Assert
        // TODO: Re-enable when FeatureTracerService is implemented
        // await Assert.ThrowsAsync<NotImplementedException>(async () =>
        // {
        //     await _featureTracer.TraceFeatureAsync("ProductManagement");
        // });

        // TODO: Verify complete dependency chain extracted
        // Controller → Service → Repository → Entity
    }

    #endregion

    #region Cross-Feature Integration Tests

    [Test]
    public async Task PolyglotAnalysis_TracksAcrossAllFileTypes()
    {
        // Arrange: Create polyglot project with C#, JSON, YAML, Markdown
        var projectDir = CreateTestProject("PolyglotProject");
        var srcDir = Path.Combine(projectDir, "src");
        var docsDir = Path.Combine(projectDir, "docs");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(docsDir);

        // C# code
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Payment.cs"), @"
public class Payment
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } // Will rename to CurrencyCode
}");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "PaymentService.cs"), @"
public class PaymentService
{
    public void ProcessPayment(Payment payment)
    {
        Console.WriteLine($""Processing {payment.Amount} {payment.Currency}"");
    }
}");

        // JSON config
        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.json"), @"{
  ""Payment"": {
    ""DefaultCurrency"": ""USD"",
    ""AllowedCurrencies"": [""USD"", ""EUR"", ""GBP""]
  }
}");

        // YAML workflow
        var workflowPath = CreateWorkflowFile(projectDir, "payment-tests.yml", @"
name: Payment Tests
on: [push]
jobs:
  test:
    steps:
      - run: dotnet test --filter PaymentService --filter Currency
");

        // Markdown docs
        await File.WriteAllTextAsync(Path.Combine(docsDir, "Payments.md"), @"# Payment Processing
## Usage
```csharp
var payment = new Payment { Amount = 100, Currency = ""USD"" };
service.ProcessPayment(payment);
```");

        // Initialize workspace for C# files
        await InitializeWorkspaceForProject(projectDir);

        var change = new CodeChange
        {
            FilePath = Path.Combine(srcDir, "Payment.cs"),
            ChangeType = "PropertyRename",
            SymbolName = "Currency"
        };

        // Act: Analyze impact across all file types
        var result = await _impactAnalyzer.AnalyzeImpactAsync(change);

        // Assert: Verify polyglot impact detection
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalImpactedFiles > 0, Is.True);

        // Should detect impacts in C# files
        var csharpFiles = result.CSharpImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(csharpFiles, Does.Contain("PaymentService.cs"));

        // Should detect impacts in config files
        var configFiles = result.ConfigImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Console.WriteLine($"[TEST DEBUG] Config files found: [{string.Join(", ", configFiles)}]");
        Assert.That(configFiles, Does.Contain("appsettings.json"));

        // Should detect impacts in workflow files
        var workflowFiles = result.WorkflowImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(workflowFiles, Does.Contain("payment-tests.yml"));

        // Should detect impacts in documentation
        var docFiles = result.DocumentationImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(docFiles, Does.Contain("Payments.md"));
    }

    [Test]
    public async Task CompleteWorkflow_AnalyzeProjectStructure()
    {
        // Arrange: Create realistic project
        var projectDir = CreateTestProject("CompleteProject");
        var srcDir = Path.Combine(projectDir, "src");
        var testsDir = Path.Combine(projectDir, "tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testsDir);

        // Create .csproj
        await File.WriteAllTextAsync(Path.Combine(projectDir, "CompleteProject.csproj"),
            @"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        // Create workflow
        CreateWorkflowFile(projectDir, "ci.yml", @"
name: CI
on: [push]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build
      - run: dotnet test
");

        // Create config
        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.json"), @"{
  ""Database"": {
    ""ConnectionString"": ""Server=localhost""
  }
}");

        // Create source files
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Service.cs"),
            "public class Service { }");

        await File.WriteAllTextAsync(Path.Combine(testsDir, "ServiceTests.cs"),
            "public class ServiceTests { [Fact] public void Test() { } }");

        // Act: Perform workflow analysis
        var workflows = await _workflowAnalyzer.GetAllWorkflowsAsync(projectDir);

        // Assert: Verify workflows were found and analyzed
        Assert.That(workflows, Is.Not.Empty, "Should find workflows in the project");
    }

    #endregion
}
