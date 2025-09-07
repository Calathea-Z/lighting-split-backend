using Api.Abstractions.Receipts;
using Api.Abstractions.Transport;
using Api.Common.Interfaces;
using Api.Contracts.Reconciliation;
using Api.Data;
using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Infrastructure.Interfaces;
using Api.Mappers;
using Api.Options;
using Api.Services.Receipts;
using Api.Services.Receipts.Abstractions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Tests;

public class ReceiptServiceTests : IDisposable
{
    private readonly LightningDbContext _dbContext;
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<IParseQueue> _mockParseQueue;
    private readonly Mock<IClock> _mockClock;
    private readonly Mock<IReceiptReconciliationOrchestrator> _mockReconciler;
    private readonly IOptions<StorageOptions> _storageOptions;
    private readonly ReceiptService _receiptService;

    public ReceiptServiceTests()
    {
        // In-memory DB
        var dbOpts = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LightningDbContext(dbOpts);

        // Mocks
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockParseQueue = new Mock<IParseQueue>();
        _mockClock = new Mock<IClock>();
        _mockReconciler = new Mock<IReceiptReconciliationOrchestrator>();

        // Setup clock mock
        _mockClock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        // Options
        _storageOptions = Options.Create(new StorageOptions
        {
            ReceiptsContainer = TestHelpers.TestConstants.ReceiptsContainer,
            OverwriteOnUpload = true
        });

        // SUT
        _receiptService = new ReceiptService(
            _dbContext,
            _mockBlobServiceClient.Object,
            _mockParseQueue.Object,
            _storageOptions,
            _mockClock.Object,
            _mockReconciler.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_ShouldCreateReceipt()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemRequest>
            {
                new(
                    Label: "Test Item",
                    Qty: 2,
                    UnitPrice: 10.50m
                )
            },
            // Provide the totals explicitly to avoid relying on reconciliation logic
            SubTotal = 21.00m, // 2 * 10.50
            Total = 21.00m // No tax or tip
        };

        // Act
        var result = await _receiptService.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be(ReceiptStatus.Parsed);
        result.ItemCount.Should().Be(1);
        result.SubTotal.Should().Be(21.00m); // 2 * 10.50
        result.Total.Should().Be(21.00m); // No tax or tip

        // Verify database
        var savedReceipt = await _dbContext.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        savedReceipt.Should().NotBeNull();
        savedReceipt!.Items.Should().HaveCount(1);
        savedReceipt.Items[0].Label.Should().Be("Test Item");
        savedReceipt.Items[0].LineSubtotal.Should().Be(21.00m);
    }

    [Fact]
    public async Task CreateAsync_WithNullDto_ShouldThrowArgumentException()
    {
        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(null!))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.BodyRequired}*");
    }

    [Fact]
    public async Task CreateAsync_WithEmptyItems_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto { Items = new List<CreateReceiptItemRequest>() };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.ItemsRequired}*");
    }

    [Fact]
    public async Task CreateAsync_WithInvalidItemQuantities_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemRequest>
            {
                new(Label: "Test", Qty: 0, UnitPrice: 10m)
            }
        };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.InvalidQuantities}*");
    }

    [Fact]
    public async Task CreateAsync_WithNegativePrices_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemRequest>
            {
                new(Label: "Test", Qty: 1, UnitPrice: -10m)
            }
        };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.InvalidQuantities}*");
    }

    [Fact]
    public async Task CreateAsync_WithTaxAndDiscount_ShouldCalculateCorrectly()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemRequest>
            {
                new(
                    Label: "Test Item",
                    Qty: 2,
                    UnitPrice: 10.00m,
                    Discount: 2.00m,
                    Tax: 1.50m
                )
            },
            // Provide the totals explicitly to avoid relying on reconciliation logic
            SubTotal = 18.00m, // (2 * 10) - 2 discount
            Tax = 1.50m,
            Total = 19.50m // 18 + 1.50 tax
        };

        // Act
        var result = await _receiptService.CreateAsync(dto);

        // Assert
        result.SubTotal.Should().Be(18.00m); // (2 * 10) - 2 discount
        result.Total.Should().Be(19.50m); // 18 + 1.50 tax

        var savedReceipt = await _dbContext.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        savedReceipt!.Items[0].LineSubtotal.Should().Be(18.00m);
        savedReceipt.Items[0].LineTotal.Should().Be(19.50m);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingReceipt_ShouldReturnReceipt()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed,
            Items = new List<Api.Models.ReceiptItem>
            {
                new() { Label = "Test Item", Qty = 1, UnitPrice = 10m }
            }
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.GetByIdAsync(receipt.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(receipt.Id);
        result.Status.Should().Be(ReceiptStatus.Parsed);
        result.Items.Should().HaveCount(1);
        result.Items[0].Label.Should().Be("Test Item");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Act
        var result = await _receiptService.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WithNoFilter_ShouldReturnAllReceipts()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>
        {
            new() { Id = Guid.NewGuid(), Status = ReceiptStatus.Parsed, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Status = ReceiptStatus.Parsed, CreatedAt = DateTimeOffset.UtcNow }
        };
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync(null, 0, 10);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(r => r.CreatedAt);
    }

    [Fact]
    public async Task ListAsync_WithOwnerFilter_ShouldReturnFilteredReceipts()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>
        {
            new() { Id = Guid.NewGuid(), OwnerUserId = TestHelpers.TestConstants.TestUserId1, Status = ReceiptStatus.Parsed },
            new() { Id = Guid.NewGuid(), OwnerUserId = TestHelpers.TestConstants.TestUserId2, Status = ReceiptStatus.Parsed },
            new() { Id = Guid.NewGuid(), OwnerUserId = TestHelpers.TestConstants.TestUserId1, Status = ReceiptStatus.Parsed }
        };
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync(TestHelpers.TestConstants.TestUserId1, 0, 10);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Id == receipts[0].Id || r.Id == receipts[2].Id);
    }

    [Fact]
    public async Task ListAsync_WithPagination_ShouldRespectSkipAndTake()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>();
        for (int i = 0; i < 5; i++)
        {
            receipts.Add(new Api.Models.Receipt
            {
                Id = Guid.NewGuid(),
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync(null, 2, 2);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingReceipt_ShouldDeleteReceipt()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed,
            BlobContainer = TestHelpers.TestConstants.ReceiptsContainer,
            BlobName = TestHelpers.TestConstants.TestFileName
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Mock blob deletion
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(TestHelpers.TestConstants.ReceiptsContainer))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.GetBlobClient(TestHelpers.TestConstants.TestFileName))
            .Returns(mockBlob.Object);

        // Act
        var result = await _receiptService.DeleteAsync(receipt.Id);

        // Assert
        result.Should().BeTrue();
        var deletedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        deletedReceipt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentReceipt_ShouldReturnFalse()
    {
        // Act
        var result = await _receiptService.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #region QUARANTINE

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateTotalsAsync_WithExistingReceipt_ShouldUpdateTotals()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.PendingParse,
            Items = new List<Api.Models.ReceiptItem>
            {
                new() { Label = "Test", Qty = 2, UnitPrice = 10m, LineSubtotal = 20m, LineTotal = 20m }
            }
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var updateDto = TestHelpers.CreateValidUpdateTotalsRequest();
        

        // Act
        var result = await _receiptService.UpdateTotalsAsync(receipt.Id, updateDto);

        // Assert
        result.Should().NotBeNull();
        result!.SubTotal.Should().Be(30.00m);
        result.Tax.Should().Be(3.00m);
        result.Tip.Should().Be(6.00m);
        result.Total.Should().Be(39.00m);
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateTotalsAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var updateDto = new UpdateTotalsRequest(20m, null, null, null);

        // Act
        var result = await _receiptService.UpdateTotalsAsync(Guid.NewGuid(), updateDto);

        // Assert
        result.Should().BeNull();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task MarkParseFailedAsync_WithExistingReceipt_ShouldMarkAsFailed()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.PendingParse
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.MarkParseFailedAsync(receipt.Id, "Test error");

        // Assert
        result.Should().BeTrue();
        
        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.Status.Should().Be(ReceiptStatus.FailedParse);
        updatedReceipt.ParseError.Should().Be("Test error");
        updatedReceipt.NeedsReview.Should().BeTrue();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task MarkParseFailedAsync_WithNonExistentReceipt_ShouldReturnFalse()
    {
        // Act
        var result = await _receiptService.MarkParseFailedAsync(Guid.NewGuid(), "Test error");

        // Assert
        result.Should().BeFalse();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateRawTextAsync_WithExistingReceipt_ShouldUpdateRawText()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidUpdateRawTextRequest();

        // Act
        var result = await _receiptService.UpdateRawTextAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        
        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.RawText.Should().Be("Updated raw text content");
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateRawTextAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var dto = new UpdateRawTextRequest("Test content");

        // Act
        var result = await _receiptService.UpdateRawTextAsync(Guid.NewGuid(), dto);

        // Assert
        result.Should().BeNull();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateStatusAsync_WithExistingReceipt_ShouldUpdateStatus()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new UpdateStatusRequest(ReceiptStatus.ParsedNeedsReview);

        // Act
        var result = await _receiptService.UpdateStatusAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        
        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.Status.Should().Be(ReceiptStatus.ParsedNeedsReview);
        updatedReceipt.NeedsReview.Should().BeTrue();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateStatusAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var dto = new UpdateStatusRequest(ReceiptStatus.Parsed);

        // Act
        var result = await _receiptService.UpdateStatusAsync(Guid.NewGuid(), dto);

        // Assert
        result.Should().BeNull();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateReviewAsync_WithExistingReceipt_ShouldUpdateReviewStatus()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed,
            NeedsReview = false
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new UpdateReviewDto(true, "Needs manual review");

        // Act
        var result = await _receiptService.UpdateReviewAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        
        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.NeedsReview.Should().BeTrue();
    }

    [Fact(Skip = "Requires real Postgres (ExecuteUpdateAsync not supported by InMemory).")]
    public async Task UpdateReviewAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var dto = new UpdateReviewDto(false, null);

        // Act
        var result = await _receiptService.UpdateReviewAsync(Guid.NewGuid(), dto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateParseMetaAsync_WithExistingReceipt_ShouldUpdateParseMeta()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidUpdateParseMetaRequest();

        // Act
        var result = await _receiptService.UpdateParseMetaAsync(receipt.Id, dto);

        // Assert
        result.Should().BeTrue();
        
        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.ParsedBy.Should().Be(ParseEngine.Heuristics);
        updatedReceipt.LlmAttempted.Should().BeTrue();
        updatedReceipt.LlmAccepted.Should().BeTrue();
        updatedReceipt.LlmModel.Should().Be("gpt-4");
        updatedReceipt.ParserVersion.Should().Be("1.0.0");
        updatedReceipt.ParsedAt.Should().NotBeNull();
    }

    #endregion
    [Fact]
    public async Task UpdateParseMetaAsync_WithNonExistentReceipt_ShouldReturnFalse()
    {
        // Arrange
        var dto = TestHelpers.CreateValidUpdateParseMetaRequest();

        // Act
        var result = await _receiptService.UpdateParseMetaAsync(Guid.NewGuid(), dto);

        // Assert
        result.Should().BeFalse();
    }
}
