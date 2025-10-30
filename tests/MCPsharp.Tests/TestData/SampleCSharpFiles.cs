namespace MCPsharp.Tests.TestData;

/// <summary>
/// Sample C# code files for testing
/// </summary>
public static class SampleCSharpFiles
{
    /// <summary>
    /// Simple class with basic methods for testing method analysis
    /// </summary>
    public const string SimpleClass = @"
using System;
using System.Collections.Generic;

namespace TestProject.Services
{
    public class SampleService
    {
        private readonly ILogger _logger;

        public SampleService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string ProcessData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input.ToUpper();
        }

        public async Task<List<string>> GetDataAsync(int count)
        {
            var result = new List<string>();
            for (int i = 0; i < count; i++)
            {
                result.Add($""Item {i}"");
            }
            return await Task.FromResult(result);
        }

        public void LogMessage(string message)
        {
            _logger.LogInformation(message);
        }
    }
}";

    /// <summary>
    /// Class with inheritance and interface implementations for testing inheritance analysis
    /// </summary>
    public const string InheritanceExample = @"
using System;

namespace TestProject.Models
{
    public interface IDataProcessor
    {
        string Process(string data);
        bool CanProcess(string data);
    }

    public abstract class BaseProcessor : IDataProcessor
    {
        public abstract string Process(string data);

        public virtual bool CanProcess(string data)
        {
            return !string.IsNullOrEmpty(data);
        }
    }

    public class TextProcessor : BaseProcessor
    {
        public override string Process(string data)
        {
            return data?.Trim() ?? string.Empty;
        }

        public override bool CanProcess(string data)
        {
            return base.CanProcess(data) && data.Length > 0;
        }
    }

    public class AdvancedTextProcessor : TextProcessor
    {
        public int ProcessCount { get; private set; }

        public override string Process(string data)
        {
            ProcessCount++;
            return base.Process(data);
        }
    }
}";

    /// <summary>
    /// Complex class with method call chains for testing call analysis
    /// </summary>
    public const string CallChainExample = @"
using System;
using System.Threading.Tasks;

namespace TestProject.Workflows
{
    public class DataWorkflow
    {
        private readonly IDataValidator _validator;
        private readonly IDataProcessor _processor;
        private readonly IDataRepository _repository;

        public DataWorkflow(IDataValidator validator, IDataProcessor processor, IDataRepository repository)
        {
            _validator = validator;
            _processor = processor;
            _repository = repository;
        }

        public async Task<WorkflowResult> ExecuteWorkflowAsync(string data)
        {
            try
            {
                // Validation chain
                var validationResult = _validator.ValidateAsync(data);
                if (!validationResult.IsValid)
                {
                    return WorkflowResult.Failure(validationResult.ErrorMessage);
                }

                // Processing chain
                var processedData = _processor.ProcessAsync(validationResult.ValidatedData);
                var enrichedData = await _processor.EnrichAsync(processedData);

                // Storage chain
                var saveResult = await _repository.SaveAsync(enrichedData);
                if (!saveResult.Success)
                {
                    return WorkflowResult.Failure(saveResult.ErrorMessage);
                }

                return WorkflowResult.Success(saveResult.Id);
            }
            catch (Exception ex)
            {
                return WorkflowResult.Failure(ex.Message);
            }
        }

        public void LogStep(string step, string details)
        {
            Console.WriteLine($""[{DateTime.UtcNow:O}] {step}: {details}"");
        }

        private void Audit(string action, object data)
        {
            // Audit implementation
        }
    }
}";

    /// <summary>
    /// Large file for testing performance with substantial code
    /// </summary>
    public const string LargeFile = GenerateLargeFile();

    private static string GenerateLargeFile()
    {
        var code = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestProject.Large
{
    public class LargeService
    {
        private readonly List<string> _items = new();

        public LargeService()
        {
            InitializeItems();
        }

        private void InitializeItems()
        {
";

        // Generate many methods and properties for testing
        for (int i = 0; i < 100; i++)
        {
            code += $@"
            _items.Add(""Item {i}"");";
        }

        code += @"
        }

        public string GetItem(int index)
        {
            if (index < 0 || index >= _items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }

        public void AddItem(string item)
        {
            if (string.IsNullOrEmpty(item))
                throw new ArgumentException(""Item cannot be null or empty"", nameof(item));
            _items.Add(item);
        }

        public bool RemoveItem(string item)
        {
            return _items.Remove(item);
        }

        public int Count => _items.Count;

        public IReadOnlyList<string> GetAllItems() => _items.AsReadOnly();

        public async Task<bool> ContainsItemAsync(string item)
        {
            await Task.Delay(1); // Simulate async work
            return _items.Contains(item);
        }

        public IEnumerable<string> FindItems(Func<string, bool> predicate)
        {
            return _items.Where(predicate);
        }

        public void Clear()
        {
            _items.Clear();
        }
    }";

        // Add many small classes for better testing
        for (int i = 0; i < 20; i++)
        {
            code += $@"

    public class HelperClass{i}
    {{
        public int Value {{ get; set; }}

        public string Process(int input)
        {{
            return $""Processed {{input}} with value {{Value}}"";
        }}

        public async Task<string> ProcessAsync(int input)
        {{
            await Task.Delay(1);
            return Process(input);
        }}
    }}";
        }

        code += "\n}";

        return code;
    }

    /// <summary>
    /// File with generic types for testing generic analysis
    /// </summary>
    public const string GenericExample = @"
using System;
using System.Collections.Generic;

namespace TestProject.Generics
{
    public interface IRepository<T>
    {
        Task<T> GetByIdAsync(int id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<T> AddAsync(T item);
        Task<bool> UpdateAsync(T item);
        Task<bool> DeleteAsync(int id);
    }

    public class Repository<T> : IRepository<T> where T : class, IEntity
    {
        private readonly List<T> _items = new();

        public async Task<T> GetByIdAsync(int id)
        {
            await Task.Delay(1);
            return _items.FirstOrDefault(x => x.Id == id);
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            await Task.Delay(1);
            return _items.ToList();
        }

        public async Task<T> AddAsync(T item)
        {
            await Task.Delay(1);
            _items.Add(item);
            return item;
        }

        public async Task<bool> UpdateAsync(T item)
        {
            await Task.Delay(1);
            var existing = _items.FirstOrDefault(x => x.Id == item.Id);
            if (existing != null)
            {
                _items.Remove(existing);
                _items.Add(item);
                return true;
            }
            return false;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await Task.Delay(1);
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                _items.Remove(item);
                return true;
            }
            return false;
        }
    }

    public interface IEntity
    {
        int Id { get; set; }
        DateTime CreatedAt { get; set; }
    }

    public class User : IEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class Product : IEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}";

    /// <summary>
    /// File with error patterns for testing error handling
    /// </summary>
    public const string ErrorPatternsExample = @"
using System;
using System.IO;

namespace TestProject.Errors
{
    public class ErrorProneService
    {
        public void ThrowArgumentException(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException(""Input cannot be null or empty"", nameof(input));
        }

        public void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException(""Operation is not valid in current state"");
        }

        public string AccessFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($""File not found: {path}"");

            return File.ReadAllText(path);
        }

        public int DivideByZero(int divisor)
        {
            if (divisor == 0)
                throw new DivideByZeroException();

            return 100 / divisor;
        }
    }
}";
}