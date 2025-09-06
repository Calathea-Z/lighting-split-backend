using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Api.Abstractions.Receipts;
using Api.Common.Interfaces;
using Api.Contracts.Receipts;
using Api.Data;
using Api.Dtos.Receipts.Responses.Items;
using Api.Infrastructure.Interfaces;
using Api.Options;
using Api.Services.Receipts;
using Api.Services.Receipts.Abstractions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests;

public class ReceiptServiceUploadTests : IDisposable
{
    private readonly LightningDbContext _dbContext;
    private readonly Mock<BlobServiceClient> _blobSvc;
    private readonly Mock<IParseQueue> _parseQueue;
    private readonly Mock<IClock> _clock;
    private readonly Mock<IReceiptReconciliationOrchestrator> _reconciler;
    private readonly IOptions<StorageOptions> _storage;
    private readonly DateTimeOffset _now;
    private ReceiptService _sut;

    public ReceiptServiceUploadTests()
    {
        var opts = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LightningDbContext(opts);

        _now = DateTimeOffset.Parse("2025-09-05T12:00:00Z");
        _storage = Options.Create(new StorageOptions
        {
            ReceiptsContainer = TestHelpers.TestConstants.ReceiptsContainer,
            OverwriteOnUpload = true
        });

        _blobSvc = new Mock<BlobServiceClient>(MockBehavior.Loose);
        _parseQueue = new Mock<IParseQueue>(MockBehavior.Loose);
        _clock = new Mock<IClock>(MockBehavior.Loose);
        _reconciler = new Mock<IReceiptReconciliationOrchestrator>(MockBehavior.Loose);

        _clock.Setup(x => x.UtcNow).Returns(_now);

        _sut = new ReceiptService(
            _dbContext,
            _blobSvc.Object,
            _parseQueue.Object,
            _storage,
            _clock.Object,
            _reconciler.Object
        );
    }

    public void Dispose() => _dbContext.Dispose();

    /* ---------- helpers ---------- */

    private static IFormFile MakeFormFile(string name, string contentType, byte[] bytes)
    {
        var f = new Mock<IFormFile>();
        f.Setup(x => x.FileName).Returns(name);
        f.Setup(x => x.ContentType).Returns(contentType);
        f.Setup(x => x.Length).Returns(bytes.Length);
        f.Setup(x => x.OpenReadStream()).Returns(new MemoryStream(bytes));
        return f.Object;
    }

    private (Mock<BlobContainerClient> container, Mock<BlobClient> blob) SetupBlobHappy(string containerName = TestHelpers.TestConstants.ReceiptsContainer)
    {
        var container = new Mock<BlobContainerClient>(MockBehavior.Loose);
        var blob = new Mock<BlobClient>(MockBehavior.Loose);

        _blobSvc.Setup(x => x.GetBlobContainerClient(containerName)).Returns(container.Object);
        container.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(blob.Object);

        blob.SetupGet(b => b.Uri).Returns(new Uri($"https://test.blob.core.windows.net/{containerName}/{Guid.NewGuid()}.jpg"));
        blob.Setup(x => x.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<bool>>());
        blob.Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

        return (container, blob);
    }

    /* ---------- tests ---------- */

    [Fact]
    public async Task UploadAsync_WithValidFile_PersistsReceipt_EnqueuesParse_AndUploadsBlob()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes("ok");
        var file = MakeFormFile(TestHelpers.TestConstants.TestFileName, TestHelpers.TestConstants.TestContentType, bytes);
        var dto = new UploadReceiptItemDto
        {
            File = file,
            StoreName = "Store",
            PurchasedAt = _now.AddDays(-1),
            Notes = "n"
        };

        var (_, blob) = SetupBlobHappy();
        _parseQueue.Setup(q => q.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.UploadAsync(dto);

        // Assert: result shape
        result.Should().NotBeNull();
        result.Status.Should().Be(ReceiptStatus.PendingParse);
        result.ItemCount.Should().Be(0);
        result.Id.Should().NotBeEmpty();

        // Assert: receipt persisted
        var saved = await _dbContext.Receipts.FindAsync(result.Id);
        saved.Should().NotBeNull();
        saved!.BlobContainer.Should().Be(TestHelpers.TestConstants.ReceiptsContainer);
        saved.BlobName.Should().NotBeNullOrWhiteSpace();

        // Assert: side effects attempted
        blob.Verify(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _parseQueue.Verify(q => q.EnqueueAsync(
            It.Is<ReceiptParseMessage>(m => m.ReceiptId == result.Id.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_NullFile_Throws_AndDoesNotPersist()
    {
        var dto = new UploadReceiptItemDto { File = null! };

        await _sut.Invoking(s => s.UploadAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.FileRequired}*");

        (await _dbContext.Receipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UploadAsync_EmptyFile_Throws_AndDoesNotPersist()
    {
        var f = new Mock<IFormFile>();
        f.Setup(x => x.Length).Returns(0);

        await _sut.Invoking(s => s.UploadAsync(new UploadReceiptItemDto { File = f.Object }))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.FileRequired}*");

        (await _dbContext.Receipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UploadAsync_TooLarge_Throws_AndDoesNotPersist()
    {
        var f = new Mock<IFormFile>();
        f.Setup(x => x.Length).Returns(20_000_001);

        await _sut.Invoking(s => s.UploadAsync(new UploadReceiptItemDto { File = f.Object }))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.FileTooLarge}*");

        (await _dbContext.Receipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UploadAsync_SameFileTwice_CreatesDistinctReceipts()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("same");
        var f1 = MakeFormFile("dup.jpg", TestHelpers.TestConstants.TestContentType, bytes);
        var f2 = MakeFormFile("dup.jpg", TestHelpers.TestConstants.TestContentType, bytes);

        SetupBlobHappy();
        _parseQueue.Setup(q => q.EnqueueAsync(It.IsAny<ReceiptParseMessage>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var r1 = await _sut.UploadAsync(new UploadReceiptItemDto
        {
            File = f1,
            StoreName = "Store A",
            PurchasedAt = _now,
            Notes = "n1"
        });

        SetupBlobHappy(); // new blob for second upload
        var r2 = await _sut.UploadAsync(new UploadReceiptItemDto
        {
            File = f2,
            StoreName = "Store B",
            PurchasedAt = _now,
            Notes = "n2"
        });

        r1.Id.Should().NotBeEmpty();
        r2.Id.Should().NotBeEmpty();
        r2.Id.Should().NotBe(r1.Id);

        var e1 = await _dbContext.Receipts.FindAsync(r1.Id);
        var e2 = await _dbContext.Receipts.FindAsync(r2.Id);
        e1!.BlobName.Should().NotBeNullOrWhiteSpace();
        e2!.BlobName.Should().NotBeNullOrWhiteSpace();
        e1.BlobName.Should().NotBe(e2.BlobName);
    }
}
