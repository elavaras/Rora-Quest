using Xunit;

public class TaskAssetStorageTests
{
    private static RoraQuestService CreateService(RecordingAssetStorage storage)
    {
        var state = new AppState();
        var store = new InMemoryRoraQuestStore(state);
        return new RoraQuestService(store, storage);
    }

    private static CreateTaskRequest MinimalCreateRequest(string title = "Task") =>
        new(
            Title: title,
            Description: null,
            CategoryId: null,
            SubCategoryId: null,
            PlannedWeekStart: null,
            PlannedDate: null,
            DueDate: null,
            StartAt: null,
            EndAt: null,
            Priority: null,
            Status: null,
            AssignedTo: null);

    [Fact]
    public async Task CreateAssetAsync_FromDataUrl_UploadsDecodedBytes()
    {
        var storage = new RecordingAssetStorage();
        var svc = CreateService(storage);
        var task = svc.CreateTask("user1", MinimalCreateRequest());

        var result = await svc.CreateAssetAsync(
            "user1",
            task.Id,
            new CreateAssetRequest(
                AssetType: "DiagramImage",
                StoragePathOrUrl: "data:image/png;base64,AAEC",
                FileName: "diagram.png",
                ContentType: null,
                SizeBytes: 3));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://example.blob.core.windows.net/task-assets/diagram.png", result.Value!.StoragePathOrUrl);
        Assert.Equal("diagram.png", storage.UploadedFileName);
        Assert.Equal("image/png", storage.UploadedContentType);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02 }, storage.UploadedBytes);
        Assert.Single(task.Assets);
        Assert.Equal(result.Value.StoragePathOrUrl, task.Assets[0].StoragePathOrUrl);
    }

    [Fact]
    public async Task CreateAssetAsync_StreamUpload_StoresReturnedUrl()
    {
        var storage = new RecordingAssetStorage();
        var svc = CreateService(storage);
        var task = svc.CreateTask("user1", MinimalCreateRequest());
        await using var stream = new MemoryStream(new byte[] { 0x10, 0x20, 0x30 });

        var result = await svc.CreateAssetAsync(
            "user1",
            task.Id,
            "DiagramImage",
            "diagram.png",
            "image/png",
            stream.Length,
            stream);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(storage.Url, result.Value!.StoragePathOrUrl);
        Assert.Equal("diagram.png", storage.UploadedFileName);
        Assert.Equal("image/png", storage.UploadedContentType);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, storage.UploadedBytes);
        Assert.Single(task.Assets);
    }

    [Fact]
    public async Task DeleteAssetAsync_DeletesStoredBlobReference()
    {
        var storage = new RecordingAssetStorage();
        var svc = CreateService(storage);
        var task = svc.CreateTask("user1", MinimalCreateRequest());
        var created = await svc.CreateAssetAsync(
            "user1",
            task.Id,
            "DiagramImage",
            "diagram.png",
            "image/png",
            3,
            new MemoryStream(new byte[] { 0x10, 0x20, 0x30 }));

        var deleted = await svc.DeleteAssetAsync("user1", task.Id, created.Value!.Id);

        Assert.True(deleted);
        Assert.Equal(created.Value.StoragePathOrUrl, storage.DeletedStoragePathOrUrl);
        Assert.Empty(task.Assets);
    }

    private sealed class RecordingAssetStorage : ITaskAssetStorage
    {
        public string Url { get; init; } = "https://example.blob.core.windows.net/task-assets/diagram.png";
        public string? UploadedFileName { get; private set; }
        public string? UploadedContentType { get; private set; }
        public byte[]? UploadedBytes { get; private set; }
        public string? DeletedStoragePathOrUrl { get; private set; }

        public Task<string> UploadAsync(
            string userId,
            Guid taskId,
            string fileName,
            string? contentType,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            UploadedFileName = fileName;
            UploadedContentType = contentType;
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            UploadedBytes = buffer.ToArray();
            return Task.FromResult(Url);
        }

        public Task DeleteAsync(string storagePathOrUrl, CancellationToken cancellationToken = default)
        {
            DeletedStoragePathOrUrl = storagePathOrUrl;
            return Task.CompletedTask;
        }
    }
}
