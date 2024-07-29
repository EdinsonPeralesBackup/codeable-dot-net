  namespace CachedInventory;

  using System.Text.Json;

  public interface IOperationsTracker
  {
    Task<int[]> GetActionsByProductId(int productId);
    Task<string> CreateOperationsTracker(DateTime time, int productId,int action);
    Task FailUpdateByProductId(int productId);
    Task FailUpdateByOperationId(string operationId);
    Task RemoveCache();
  }
  public class OperationsTracker : IOperationsTracker
  {
    private static readonly string FileName = "operations-tracker.json";
    private class Operation
    {
        public required string Id { get; set; }
        public DateTime Time { get; set; }
        public bool Ok { get; set; } = true;
        public int ProductId { get; set; }
        public int Action { get; set; }
        public bool InCache { get;set; } = true;
    }
    public async Task<int[]> GetActionsByProductId(int productId)
    {
      try
        {
          var operations = await ReadOperationsFromFile();
          return operations
              .Where(op => op.ProductId == productId && op.Ok && op.InCache)
              .Select(op => op.Action)
              .ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    public async Task<string> CreateOperationsTracker(DateTime time, int productId, int action)
    {
        var operations = await ReadOperationsFromFile();
        var newOperation = new Operation
        {
            Id = Guid.NewGuid().ToString(),
            Time = time,
            ProductId = productId,
            Action = action
        };
        operations.Add(newOperation);
        await WriteOperationsToFile(operations);
        return newOperation.Id;
    }

    public async Task FailUpdateByProductId(int productId)
    {
        var operations = await ReadOperationsFromFile();
        foreach (var operation in operations.Where(op => op.ProductId == productId))
        {
            operation.Ok = false;
        }
        await WriteOperationsToFile(operations);
    }

    public async Task FailUpdateByOperationId(string operationId)
    {
        var operations = await ReadOperationsFromFile();
        var targetOperation = operations.FirstOrDefault(op => op.Id == operationId);
        if (targetOperation != null)
        {
          targetOperation.Ok = false;
          await WriteOperationsToFile(operations);
        }
    }

    public async Task RemoveCache()
    {
        var operations = await ReadOperationsFromFile();
        foreach (var operation in operations)
        {
            operation.InCache = false;
        }
        await WriteOperationsToFile(operations);
    }

    private async Task<List<Operation>> ReadOperationsFromFile()
    {
        if (!File.Exists(FileName))
        {
            return new List<Operation>();
        }

        var json = await File.ReadAllTextAsync(FileName);
        return JsonSerializer.Deserialize<List<Operation>>(json) ?? new List<Operation>();
    }

    private async Task WriteOperationsToFile(List<Operation> operations)
    {
        var json = JsonSerializer.Serialize(operations, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(FileName, json);
    }
  }
