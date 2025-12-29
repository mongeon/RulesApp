using Azure;
using Azure.Data.Tables;

namespace RulesApp.Api.Services;

public interface ITableStore
{
    Task<T?> GetEntityAsync<T>(string tableName, string partitionKey, string rowKey, CancellationToken ct = default) where T : class, ITableEntity, new();
    Task UpsertEntityAsync<T>(string tableName, T entity, CancellationToken ct = default) where T : ITableEntity;
    Task<List<T>> QueryAsync<T>(string tableName, string? filter = null, CancellationToken ct = default) where T : class, ITableEntity, new();
}

public class TableStore : ITableStore
{
    private readonly TableServiceClient _client;

    public TableStore(TableServiceClient client)
    {
        _client = client;
    }

    public async Task<T?> GetEntityAsync<T>(string tableName, string partitionKey, string rowKey, CancellationToken ct = default) 
        where T : class, ITableEntity, new()
    {
        var table = _client.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync(ct);
        
        try
        {
            var response = await table.GetEntityAsync<T>(partitionKey, rowKey, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertEntityAsync<T>(string tableName, T entity, CancellationToken ct = default) 
        where T : ITableEntity
    {
        var table = _client.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync(ct);
        await table.UpsertEntityAsync(entity, cancellationToken: ct);
    }

    public async Task<List<T>> QueryAsync<T>(string tableName, string? filter = null, CancellationToken ct = default) 
        where T : class, ITableEntity, new()
    {
        var table = _client.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync(ct);
        
        var results = new List<T>();
        await foreach (var entity in table.QueryAsync<T>(filter, cancellationToken: ct))
        {
            results.Add(entity);
        }
        return results;
    }
}
