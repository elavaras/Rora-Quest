using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

public interface ITaskAssetStorage
{
    Task<string> UploadAsync(
        string userId,
        Guid taskId,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePathOrUrl, CancellationToken cancellationToken = default);
}

public sealed class NullTaskAssetStorage : ITaskAssetStorage
{
    public Task<string> UploadAsync(
        string userId,
        Guid taskId,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Blob storage is not configured. Set Storage__ConnectionString.");
    }

    public Task DeleteAsync(string storagePathOrUrl, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class BlobTaskAssetStorage : ITaskAssetStorage
{
    private readonly BlobContainerClient _container;

    public BlobTaskAssetStorage(string connectionString, string containerName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A blob storage connection string is required.", nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException("A blob container name is required.", nameof(containerName));
        }

        _container = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
    }

    public async Task<string> UploadAsync(
        string userId,
        Guid taskId,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var blobName = BuildBlobName(userId, taskId, fileName);
        var blob = _container.GetBlobClient(blobName);
        var headers = new BlobHttpHeaders
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = headers },
            cancellationToken).ConfigureAwait(false);

        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string storagePathOrUrl, CancellationToken cancellationToken = default)
    {
        if (!TryResolveBlobName(storagePathOrUrl, out var blobName))
        {
            return;
        }

        await _container.DeleteBlobIfExistsAsync(
            blobName,
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string BuildBlobName(string userId, Guid taskId, string fileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        return $"users/{userId}/tasks/{taskId:N}/{Guid.NewGuid():N}{extension}";
    }

    private bool TryResolveBlobName(string storagePathOrUrl, out string blobName)
    {
        blobName = "";
        if (!Uri.TryCreate(storagePathOrUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var prefix = $"{_container.Name}/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        blobName = Uri.UnescapeDataString(path[prefix.Length..]);
        return !string.IsNullOrWhiteSpace(blobName);
    }
}
