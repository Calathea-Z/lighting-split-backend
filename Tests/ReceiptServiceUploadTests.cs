using Api.Contracts;
using Api.Data;
using Api.Dtos;
using Api.Interfaces;
using Api.Options;
using Api.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests;

public class ReceiptServiceUploadTests : IDisposable
{
    private readonly LightningDbContext _dbContext;
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<IParseQueue> _mockParseQueue;
    private readonly ReceiptService _receiptService;
    private readonly IOptions<StorageOptions> _storageOptions;

    public ReceiptServiceUploadTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new LightningDbContext(options);

        // Options (config-driven container name, etc.)
        _storageOptions = Options.Create(new StorageOptions
        {
            ReceiptsContainer = "receipts",
            OverwriteOnUpload = true
        });

        // Setup mocks
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockParseQueue = new Mock<IParseQueue>();

        // Create service instance (inject options)
        _receiptService = new ReceiptService(
            _dbContext,
            _mockBlobServiceClient.Object,
            _mockParseQueue.Object,
            _storageOptions
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task UploadAsync_WithValidFile_ShouldUploadAndEnqueue()
    {
        // Arrange
        var fileContent = "test image content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(fileContent.Length);
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var dto = new UploadReceiptItemDto
        {
            File = mockFile.Object,
            StoreName = "Test Store",
            PurchasedAt = DateTimeOffset.UtcNow,
            Notes = "Test notes"
        };

        // Mock blob storage
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        var mockBlobUri = new Uri("https://test.blob.core.windows.net/receipts/test.jpg");
        
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient("receipts"))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        mockContainer.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlob.Object);
        mockBlob.Setup(x => x.Uri).Returns(mockBlobUri);
        mockBlob.Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<bool>>());
        mockBlob.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

        // Mock parse queue
        _mockParseQueue.Setup(x => x.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _receiptService.UploadAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be("PendingParse");

        // Verify database
        var savedReceipt = await _dbContext.Receipts.FindAsync(result.Id);
        savedReceipt.Should().NotBeNull();
        savedReceipt!.BlobContainer.Should().Be("receipts");
        savedReceipt.BlobName.Should().NotBeEmpty();
        savedReceipt.OriginalFileUrl.Should().Be(mockBlobUri.ToString());

        // Verify parse queue was called
        _mockParseQueue.Verify(x => x.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WithNullFile_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new UploadReceiptItemDto { File = null };

        // Act & Assert
        await _receiptService.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*File is required*");
    }

    [Fact]
    public async Task UploadAsync_WithEmptyFile_ShouldThrowArgumentException()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(0);
        
        var dto = new UploadReceiptItemDto { File = mockFile.Object };

        // Act & Assert
        await _receiptService.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*File is required*");
    }

    [Fact]
    public async Task UploadAsync_WithFileTooLarge_ShouldThrowArgumentException()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(25_000_000); // 25MB
        
        var dto = new UploadReceiptItemDto { File = mockFile.Object };

        // Act & Assert
        await _receiptService.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*File too large*");
    }

    [Fact]
    public async Task UploadAsync_WithBlobUploadFailure_ShouldStillCreateReceipt()
    {
        // Arrange
        var fileContent = "test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(fileContent.Length);
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var dto = new UploadReceiptItemDto { File = mockFile.Object };

        // Mock blob storage with failure
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient("receipts"))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        mockContainer.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlob.Object);
        mockBlob.Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<bool>>());
        mockBlob.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Blob upload failed"));

        // Act & Assert
        await _receiptService.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<Exception>()
            .WithMessage("*Blob upload failed*");
    }

    [Fact]
    public async Task UploadAsync_WithParseQueueFailure_ShouldStillCreateReceipt()
    {
        // Arrange
        var fileContent = "test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(fileContent.Length);
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var dto = new UploadReceiptItemDto { File = mockFile.Object };

        // Mock blob storage
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        var mockBlobUri = new Uri("https://test.blob.core.windows.net/receipts/test.jpg");
        
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient("receipts"))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        mockContainer.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlob.Object);
        mockBlob.Setup(x => x.Uri).Returns(mockBlobUri);
        mockBlob.Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<bool>>());
        mockBlob.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

        // Mock parse queue with failure
        _mockParseQueue.Setup(x => x.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Queue failure"));

        // Act & Assert
        await _receiptService.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<Exception>()
            .WithMessage("*Queue failure*");
    }

    [Fact]
    public async Task UploadAsync_ShouldSetCorrectMetadata()
    {
        // Arrange
        var fileContent = "test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(fileContent.Length);
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var purchasedAt = DateTimeOffset.UtcNow;
        var dto = new UploadReceiptItemDto
        {
            File = mockFile.Object,
            StoreName = "Test Store",
            PurchasedAt = purchasedAt,
            Notes = "Test notes"
        };

        // Mock blob storage
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        var mockBlobUri = new Uri("https://test.blob.core.windows.net/receipts/test.jpg");
        
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient("receipts"))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        mockContainer.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlob.Object);
        mockBlob.Setup(x => x.Uri).Returns(mockBlobUri);
        mockBlob.Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<bool>>());
        mockBlob.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

        _mockParseQueue.Setup(x => x.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _receiptService.UploadAsync(dto);

        // Assert
        result.Should().NotBeNull();

        // Verify blob upload was called with correct metadata
        mockBlob.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(options => 
                options.HttpHeaders.ContentType == "image/jpeg" &&
                options.Metadata.ContainsKey("storeName") &&
                options.Metadata["storeName"] == "Test Store" &&
                options.Metadata.ContainsKey("notes") &&
                options.Metadata["notes"] == "Test notes" &&
                options.Metadata.ContainsKey("purchasedAt") &&
                options.Metadata["purchasedAt"] == purchasedAt.ToString("o")
            ),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
