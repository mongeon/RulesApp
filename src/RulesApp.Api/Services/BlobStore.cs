using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace RulesApp.Api.Services;

public interface IBlobStore
{
    Task<Stream> GetBlobAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task<string> UploadTextAsync(string path, string content, CancellationToken ct = default);
}

public class BlobStore : IBlobStore
{
    private readonly BlobServiceClient _client;
    private readonly string _containerName;

    public BlobStore(BlobServiceClient client, string containerName = "rules-data")
    {
        _client = client;
        _containerName = containerName;
    }

    public async Task<Stream> GetBlobAsync(string path, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        var blob = container.GetBlobClient(path);
        
        // Fully buffer the blob into memory to work around Azurite streaming bugs.
        // Azurite fails on large streaming downloads with "response ended prematurely" errors.
        // This is acceptable for PDFs (typically < 10MB) in local dev.
        var memoryStream = new MemoryStream();
        await blob.DownloadToAsync(memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        var blob = container.GetBlobClient(path);
        return await blob.ExistsAsync(ct);
    }

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        
        var blob = container.GetBlobClient(path);
        
        // Buffer the incoming stream to ensure a known content length for Azurite.
        // Some emulator versions return 500 on chunked uploads without a length.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blob.UploadAsync(BinaryData.FromBytes(buffer.ToArray()), options, ct);
    }

    public async Task<string> UploadTextAsync(string path, string content, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, ct);
        
        return blob.Uri.ToString();
    }
}
