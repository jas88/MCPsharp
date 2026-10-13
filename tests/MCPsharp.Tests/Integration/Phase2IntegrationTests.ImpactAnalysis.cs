using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class Phase2IntegrationTests
{
    #region Impact Analysis Integration Tests

    [Test]
    public async Task ImpactAnalysis_ModelChange_DetectsMultiFileImpact()
    {
        // Arrange: Create multi-layer architecture
        var projectDir = CreateTestProject("ImpactProject");
        var srcDir = Path.Combine(projectDir, "src");
        Directory.CreateDirectory(srcDir);

        // Create model
        await File.WriteAllTextAsync(Path.Combine(srcDir, "User.cs"), @"
namespace MyApp.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; } // This will change to string
}");

        // Create repository
        await File.WriteAllTextAsync(Path.Combine(srcDir, "UserRepository.cs"), @"
namespace MyApp.Data;

using MyApp.Models;

public class UserRepository
{
    public User GetById(int id) => throw new NotImplementedException();
    public List<User> GetByAgeRange(int minAge, int maxAge) => throw new NotImplementedException();
}");

        // Create service
        await File.WriteAllTextAsync(Path.Combine(srcDir, "UserService.cs"), @"
namespace MyApp.Services;

using MyApp.Models;
using MyApp.Data;

public class UserService
{
    private readonly UserRepository _repository;

    public void ValidateUser(User user)
    {
        if (user.Age < 0 || user.Age > 150)
            throw new ArgumentException(""Invalid age"");
    }

    public List<User> GetAdults() => _repository.GetByAgeRange(18, 150);
}");

        // Create controller
        await File.WriteAllTextAsync(Path.Combine(srcDir, "UserController.cs"), @"
namespace MyApp.Controllers;

using MyApp.Services;
using MyApp.Models;

public class UserController
{
    private readonly UserService _service;

    public IActionResult Create(User user)
    {
        _service.ValidateUser(user);
        return Created();
    }

    public IActionResult GetAdults() => Ok(_service.GetAdults());
}");

        // Initialize workspace for C# files
        await InitializeWorkspaceForProject(projectDir);

        var change = new CodeChange
        {
            FilePath = Path.Combine(srcDir, "User.cs"),
            ChangeType = "PropertyTypeChange",
            SymbolName = "Age"
        };

        // Act: Analyze impact of the change
        var result = await _impactAnalyzer.AnalyzeImpactAsync(change);

        // Assert: Verify all affected files are detected
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalImpactedFiles > 0, Is.True);

        // Should detect impacts in related C# files
        var csharpImpacts = result.CSharpImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(csharpImpacts, Does.Contain("UserRepository.cs"));
        Assert.That(csharpImpacts, Does.Contain("UserService.cs"));
        Assert.That(csharpImpacts, Does.Contain("UserController.cs"));
    }

    [Test]
    public async Task ImpactAnalysis_InterfaceChange_DetectsImplementations()
    {
        // Arrange: Create interface with multiple implementations
        var projectDir = CreateTestProject("InterfaceImpactProject");
        var srcDir = Path.Combine(projectDir, "src");
        Directory.CreateDirectory(srcDir);

        await File.WriteAllTextAsync(Path.Combine(srcDir, "IPaymentProcessor.cs"), @"
public interface IPaymentProcessor
{
    void ProcessPayment(decimal amount, string currency);
}");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "CreditCardProcessor.cs"), @"
public class CreditCardProcessor : IPaymentProcessor
{
    public void ProcessPayment(decimal amount, string currency) { }
}");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "PayPalProcessor.cs"), @"
public class PayPalProcessor : IPaymentProcessor
{
    public void ProcessPayment(decimal amount, string currency) { }
}");

        await File.WriteAllTextAsync(Path.Combine(srcDir, "PaymentService.cs"), @"
public class PaymentService
{
    private readonly IPaymentProcessor _processor;
    public void MakePayment(decimal amount) => _processor.ProcessPayment(amount, ""USD"");
}");

        // Initialize workspace for C# files
        await InitializeWorkspaceForProject(projectDir);

        var change = new CodeChange
        {
            FilePath = Path.Combine(srcDir, "IPaymentProcessor.cs"),
            ChangeType = "MethodSignatureChange",
            SymbolName = "ProcessPayment"
        };

        // Act: Analyze impact of the interface change
        var result = await _impactAnalyzer.AnalyzeImpactAsync(change);

        // Assert: Verify both implementations and callers are detected
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalImpactedFiles > 0, Is.True);

        // Should detect both implementations
        var csharpImpacts = result.CSharpImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();

        Assert.That(csharpImpacts, Does.Contain("CreditCardProcessor.cs"));
        Assert.That(csharpImpacts, Does.Contain("PayPalProcessor.cs"));
        Assert.That(csharpImpacts, Does.Contain("PaymentService.cs"));
    }

    [Test]
    public async Task ImpactAnalysis_ConfigChange_DetectsCodeReferences()
    {
        // Arrange: Create service referencing config
        var projectDir = CreateTestProject("ConfigImpactProject");

        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.json"), @"{
  ""EmailService"": {
    ""SmtpServer"": ""smtp.example.com"",
    ""Port"": 587
  }
}");

        var srcDir = Path.Combine(projectDir, "src");
        Directory.CreateDirectory(srcDir);

        await File.WriteAllTextAsync(Path.Combine(srcDir, "EmailService.cs"), @"
public class EmailService
{
    private readonly IConfiguration _config;

    public void SendEmail(string to, string subject)
    {
        var server = _config[""EmailService:SmtpServer""];
        var port = int.Parse(_config[""EmailService:Port""]);
    }
}");

        // Initialize workspace for C# files
        await InitializeWorkspaceForProject(projectDir);

        var change = new CodeChange
        {
            FilePath = Path.Combine(projectDir, "appsettings.json"),
            ChangeType = "ConfigKeyRename",
            SymbolName = "EmailService:SmtpServer"
        };

        // Act: Analyze impact of the config change
        var result = await _impactAnalyzer.AnalyzeImpactAsync(change);

        // Assert: Verify EmailService.cs is detected as affected
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalImpactedFiles > 0, Is.True);

        // Should detect config impacts
        var configImpacts = result.ConfigImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(configImpacts, Does.Contain("appsettings.json"));

        // Should detect code references to config
        var csharpImpacts = result.CSharpImpacts.Select(i => Path.GetFileName(i.FilePath)).ToList();
        Assert.That(csharpImpacts, Does.Contain("EmailService.cs"));
    }

    #endregion
}
