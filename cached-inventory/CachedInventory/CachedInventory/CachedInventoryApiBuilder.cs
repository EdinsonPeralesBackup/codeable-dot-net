namespace CachedInventory;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

public class ProductCacheItem
{
    public int ProductId { get; set; }
    public int Stock { get; set; }
}
public static class CachedInventoryApiBuilder
{
  private static readonly ConcurrentDictionary<int, ProductCacheItem> ProductCache = new();
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddScoped<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    builder.Services.AddScoped<IOperationsTracker, OperationsTracker>();
    builder.Services.AddMemoryCache();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.MapGet(
        "/stock/{productId:int}",
        async (
          [FromServices] IWarehouseStockSystemClient client,
          [FromServices] IOperationsTracker tracker,
          int productId) =>
          await GetStockWithCache(client, tracker, productId))
      .WithName("GetStock")
      .WithOpenApi();

    app.MapPost(
        "/stock/retrieve",
        async (
          [FromServices] IWarehouseStockSystemClient client,
          [FromServices] IOperationsTracker tracker,
          [FromBody] RetrieveStockRequest req) =>
        {
          var stock = await GetStockWithCache(client, tracker, req.ProductId);
          if (stock < req.Amount)
          {
            return Results.BadRequest("Not enough stock.");
          }
          var operationId = await tracker.CreateOperationsTracker(DateTime.UtcNow, req.ProductId, -req.Amount);
          _ = Task.Run(async () =>
          {
            try
            {
              await client.UpdateStock(req.ProductId, stock - req.Amount);
            }
            catch
            {
              await tracker.FailUpdateByOperationId(operationId);
            }
          });
          return Results.Ok();
        })
      .WithName("RetrieveStock")
      .WithOpenApi();


    app.MapPost(
        "/stock/restock",
        async (
          [FromServices] IWarehouseStockSystemClient client,
          [FromServices] IOperationsTracker tracker,
          [FromBody] RestockRequest req) =>
        {
          var stock = await GetStockWithCache(client, tracker, req.ProductId);
          var operationId = await tracker.CreateOperationsTracker(DateTime.UtcNow, req.ProductId, req.Amount);
          _ = Task.Run(async () =>
          {
            try
            {
              await client.UpdateStock(req.ProductId, stock + req.Amount);
            }
            catch
            {
              await tracker.FailUpdateByOperationId(operationId);
            }
          });
          return Results.Ok();
        })
      .WithName("Restock")
      .WithOpenApi();

    app.Lifetime.ApplicationStopping.Register(()=>
    {
      using var scope = app.Services.CreateScope();
      var trackerService = scope.ServiceProvider.GetRequiredService<IOperationsTracker>();
      trackerService.RemoveCache().GetAwaiter().GetResult();
    });
    return app;
  }
  public static async Task<int> GetStockWithCache(
    IWarehouseStockSystemClient client,
    IOperationsTracker tracker,
    int productId)
    {
      if (ProductCache.TryGetValue(productId, out var cacheItem))
        {
            var actions = await tracker.GetActionsByProductId(productId);
            return cacheItem.Stock + actions.Sum();
        }
        else
        {
            var stock = await client.GetStock(productId);
            ProductCache.TryAdd(productId, new ProductCacheItem { ProductId = productId, Stock = stock });
            return stock;
        }
    }
}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
