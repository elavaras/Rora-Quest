using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

/// <summary>
/// Abstracts file upload to a remote blob store.
/// The in-process default (<see cref="NoOpBlobStorageService"/>) is used when no Azure Blob
/// Storage connection string is configured; it simply returns a placeholder path so the app
/// remains functional in local development without any cloud dependency.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a file stream to the blob store and returns the public URL of the stored object.
    /// </summary>
    /// <param name="stream">The file data to upload.</param>
    /// <param name="fileName">The original file name (used to derive the blob name and content type).</param>
    /// <param name="contentType">MIME type of the file, e.g. "image/png".</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>The absolute URL of the uploaded blob.</returns>
    Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob by its URL. Fails silently if the blob does not exist.
    /// </summary>
    Task DeleteAsync(string blobUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Azure Blob Storage implementation.  Requires <see cref="BlobStorageOptions"/> to be bound
/// from configuration (section "AzureBlobStorage").
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(BlobStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("AzureBlobStorage:ConnectionString is not configured.");

        var containerName = string.IsNullOrWhiteSpace(options.ContainerName) ? "rora-quest-assets" : options.ContainerName;
        var serviceClient = new BlobServiceClient(options.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(containerName);
        _container.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        // Generate a unique blob name to avoid collisions while preserving the original extension.
        var ext = Path.GetExtension(fileName);
        var blobName = $"{Guid.NewGuid()}{ext}";

        var blobClient = _container.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string blobUrl, CancellationToken cancellationToken = default)
    {
        // Derive the blob name from the URL by stripping the container prefix.
        if (!Uri.TryCreate(blobUrl, UriKind.Absolute, out var uri)) return;
        var blobName = uri.Segments.Length > 2
            ? string.Join("", uri.Segments[2..])
            : Path.GetFileName(uri.AbsolutePath);

        var blobClient = _container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Fallback implementation used when Azure Blob Storage is not configured.
/// Returns a placeholder URL so that local development works without cloud dependencies.
/// </summary>
public sealed class NoOpBlobStorageService : IBlobStorageService
{
    public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        // Return a localhost placeholder that makes it obvious no real upload occurred.
        return Task.FromResult($"http://localhost/blob-storage-not-configured/{fileName}");
    }

    public Task DeleteAsync(string blobUrl, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Configuration options for Azure Blob Storage, bound from the "AzureBlobStorage" section.
/// </summary>
public sealed class BlobStorageOptions
{
    /// <summary>
    /// Azure Storage account connection string.
    /// Example: "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Name of the blob container. Defaults to "rora-quest-assets" when not set.
    /// </summary>
    public string? ContainerName { get; set; }
}
