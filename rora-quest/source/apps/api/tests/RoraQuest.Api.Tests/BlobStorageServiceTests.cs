using Xunit;

public class BlobStorageServiceTests
{
    [Fact]
    public async Task NoOpBlobStorageService_ReturnsPlaceholderUrl()
    {
        var service = new NoOpBlobStorageService();

        var url = await service.UploadAsync(
            new MemoryStream([1, 2, 3]),
            "Blob_Storage1.jpeg",
            "image/jpeg");

        Assert.Equal("http://localhost/blob-storage-not-configured/Blob_Storage1.jpeg", url);
    }

    [Fact]
    public void BlobStorageServiceFactory_UsesFallbackInDevelopment()
    {
        var service = BlobStorageServiceFactory.Create(new BlobStorageOptions(), allowNoOpFallback: true);

        Assert.IsType<NoOpBlobStorageService>(service);
    }

    [Fact]
    public void BlobStorageServiceFactory_ThrowsOutsideDevelopment_WhenConnectionStringMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BlobStorageServiceFactory.Create(new BlobStorageOptions(), allowNoOpFallback: false));

        Assert.Contains("AzureBlobStorage__ConnectionString", ex.Message);
    }
}
