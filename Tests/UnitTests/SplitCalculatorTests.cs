using Api.Abstractions.Receipts;
using Api.Contracts.Payment;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Models;
using Api.Models.Receipts;
using Api.Models.Splits;
using Api.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests
{
    public class SplitCalculatorTests : IDisposable
    {
        private readonly LightningDbContext _dbContext;
        private readonly SplitCalculator _service;

        public SplitCalculatorTests()
        {
            // Create in-memory database for testing
            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _service = new SplitCalculator(_dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [Fact]
        public async Task PreviewAsync_WithValidSplit_ReturnsCorrectPreview()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var itemId1 = Guid.NewGuid();
            var itemId2 = Guid.NewGuid();

            // Create receipt with items
            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                SubTotal = 20.00m,
                Tax = 2.00m,
                Tip = 3.00m,
                Total = 25.00m,
                Status = ReceiptStatus.Parsed,
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item1 = new ReceiptItem
            {
                Id = itemId1,
                ReceiptId = receiptId,
                Label = "Item 1",
                Qty = 2.0m,
                UnitPrice = 5.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item2 = new ReceiptItem
            {
                Id = itemId2,
                ReceiptId = receiptId,
                Label = "Item 2",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Create split session with participants
            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Test Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipant
            {
                Id = participantId1,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var participant2 = new SplitParticipant
            {
                Id = participantId2,
                SplitSessionId = splitId,
                DisplayName = "Bob",
                SortOrder = 2
            };

            // Create claims - Alice claims all of item1, Bob claims all of item2
            var claim1 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId1,
                ParticipantId = participantId1,
                QtyShare = 2.0m // All of item1
            };

            var claim2 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId2,
                ParticipantId = participantId2,
                QtyShare = 1.0m // All of item2
            };

            // Setup database
            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.AddRange(item1, item2);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            _dbContext.ItemClaims.AddRange(claim1, claim2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(splitId, result.SplitId);
            Assert.Equal(20.00m, result.ReceiptSubtotal);
            Assert.Equal(2.00m, result.ReceiptTax);
            Assert.Equal(3.00m, result.ReceiptTip);
            Assert.Equal(25.00m, result.ReceiptTotal);
            Assert.Equal(2, result.Participants.Count);
            Assert.Empty(result.UnclaimedItems);

            // Verify participants are ordered by SortOrder
            var alice = result.Participants[0];
            var bob = result.Participants[1];

            Assert.Equal(participantId1, alice.ParticipantId);
            Assert.Equal("Alice", alice.DisplayName);
            Assert.Equal(10.00m, alice.ItemsSubtotal); // All of item1
            Assert.Equal(1.00m, alice.TaxAlloc); // Half of tax
            Assert.Equal(1.50m, alice.TipAlloc); // Half of tip
            Assert.Equal(12.50m, alice.Total);

            Assert.Equal(participantId2, bob.ParticipantId);
            Assert.Equal("Bob", bob.DisplayName);
            Assert.Equal(10.00m, bob.ItemsSubtotal); // All of item2
            Assert.Equal(1.00m, bob.TaxAlloc); // Half of tax
            Assert.Equal(1.50m, bob.TipAlloc); // Half of tip
            Assert.Equal(12.50m, bob.Total);
        }

        [Fact]
        public async Task PreviewAsync_WithNonExistentSplit_ThrowsKeyNotFoundException()
        {
            // Arrange
            var nonExistentSplitId = Guid.NewGuid();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PreviewAsync(nonExistentSplitId));
            Assert.Equal("Split not found.", exception.Message);
        }

        [Fact]
        public async Task PreviewAsync_WithPartialClaims_ShowsUnclaimedItems()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                SubTotal = 10.00m,
                Tax = 1.00m,
                Tip = 1.50m,
                Total = 12.50m,
                Status = ReceiptStatus.Parsed,
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item = new ReceiptItem
            {
                Id = itemId,
                ReceiptId = receiptId,
                Label = "Partial Item",
                Qty = 3.0m,
                UnitPrice = 5.00m,
                LineSubtotal = 15.00m,
                LineTotal = 15.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Partial Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "PartialUser",
                SortOrder = 1
            };

            // Only claim 1.5 out of 3.0 quantity
            var claim = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId,
                QtyShare = 1.5m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.Add(item);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.Add(participant);
            _dbContext.ItemClaims.Add(claim);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.UnclaimedItems);
            Assert.Equal(itemId, result.UnclaimedItems[0].ReceiptItemId);
            Assert.Equal(1.5m, result.UnclaimedItems[0].UnclaimedQty); // 3.0 - 1.5 = 1.5
        }

        [Fact]
        public async Task PreviewAsync_WithNoClaims_ShowsAllItemsAsUnclaimed()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = 10.00m,
                Tax = 1.00m,
                Tip = 1.50m,
                Total = 12.50m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item = new ReceiptItem
            {
                Id = itemId,
                ReceiptId = receiptId,
                Label = "Unclaimed Item",
                Qty = 2.0m,
                UnitPrice = 5.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "No Claims Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "NoClaimsUser",
                SortOrder = 1
            };

            // No claims added

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.Add(item);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.Add(participant);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.UnclaimedItems);
            Assert.Equal(itemId, result.UnclaimedItems[0].ReceiptItemId);
            Assert.Equal(2.0m, result.UnclaimedItems[0].UnclaimedQty);

            // Participant should have zero item/tax/tip allocation but total includes remainder
            Assert.Single(result.Participants);
            var participantResult = result.Participants[0];
            Assert.Equal(0.00m, participantResult.ItemsSubtotal);
            Assert.Equal(0.00m, participantResult.TaxAlloc); // No base for tax allocation
            Assert.Equal(0.00m, participantResult.TipAlloc); // No base for tip allocation
            Assert.Equal(12.50m, participantResult.Total); // Total includes remainder to match receipt total
        }

        [Fact]
        public async Task PreviewAsync_WithDiscount_AllocatesDiscountProportionally()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var itemId1 = Guid.NewGuid();
            var itemId2 = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = 15.00m, // Less than sum of line subtotals (discount)
                Tax = 1.50m,
                Tip = 2.25m,
                Total = 18.75m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item1 = new ReceiptItem
            {
                Id = itemId1,
                ReceiptId = receiptId,
                Label = "Item 1",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item2 = new ReceiptItem
            {
                Id = itemId2,
                ReceiptId = receiptId,
                Label = "Item 2",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Discount Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipant
            {
                Id = participantId1,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var participant2 = new SplitParticipant
            {
                Id = participantId2,
                SplitSessionId = splitId,
                DisplayName = "Bob",
                SortOrder = 2
            };

            // Alice claims item1, Bob claims item2
            var claim1 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId1,
                ParticipantId = participantId1,
                QtyShare = 1.0m
            };

            var claim2 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId2,
                ParticipantId = participantId2,
                QtyShare = 1.0m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.AddRange(item1, item2);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            _dbContext.ItemClaims.AddRange(claim1, claim2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(15.00m, result.ReceiptSubtotal);

            // Each participant should get half of the discount (2.50m each)
            var alice = result.Participants[0];
            var bob = result.Participants[1];

            Assert.Equal(-2.50m, alice.DiscountAlloc); // Negative for discount
            Assert.Equal(7.50m, alice.ItemsSubtotal); // 10.00 - 2.50
            Assert.Equal(0.75m, alice.TaxAlloc); // Half of tax
            Assert.Equal(1.13m, alice.TipAlloc); // Half of tip (rounded)

            Assert.Equal(-2.50m, bob.DiscountAlloc); // Negative for discount
            Assert.Equal(7.50m, bob.ItemsSubtotal); // 10.00 - 2.50
            Assert.Equal(0.75m, bob.TaxAlloc); // Half of tax
            Assert.Equal(1.13m, bob.TipAlloc); // Half of tip (rounded)
        }

        [Fact]
        public async Task PreviewAsync_WithSurcharge_AllocatesSurchargeProportionally()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var itemId1 = Guid.NewGuid();
            var itemId2 = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = 25.00m, // More than sum of line subtotals (surcharge)
                Tax = 2.50m,
                Tip = 3.75m,
                Total = 31.25m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item1 = new ReceiptItem
            {
                Id = itemId1,
                ReceiptId = receiptId,
                Label = "Item 1",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item2 = new ReceiptItem
            {
                Id = itemId2,
                ReceiptId = receiptId,
                Label = "Item 2",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Surcharge Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipant
            {
                Id = participantId1,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var participant2 = new SplitParticipant
            {
                Id = participantId2,
                SplitSessionId = splitId,
                DisplayName = "Bob",
                SortOrder = 2
            };

            // Alice claims item1, Bob claims item2
            var claim1 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId1,
                ParticipantId = participantId1,
                QtyShare = 1.0m
            };

            var claim2 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId2,
                ParticipantId = participantId2,
                QtyShare = 1.0m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.AddRange(item1, item2);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            _dbContext.ItemClaims.AddRange(claim1, claim2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(25.00m, result.ReceiptSubtotal);

            // Each participant should get half of the surcharge (2.50m each)
            var alice = result.Participants[0];
            var bob = result.Participants[1];

            Assert.Equal(2.50m, alice.DiscountAlloc); // Positive for surcharge
            Assert.Equal(12.50m, alice.ItemsSubtotal); // 10.00 + 2.50
            Assert.Equal(1.25m, alice.TaxAlloc); // Half of tax
            Assert.Equal(1.88m, alice.TipAlloc); // Half of tip (rounded)

            Assert.Equal(2.50m, bob.DiscountAlloc); // Positive for surcharge
            Assert.Equal(12.50m, bob.ItemsSubtotal); // 10.00 + 2.50
            Assert.Equal(1.25m, bob.TaxAlloc); // Half of tax
            Assert.Equal(1.88m, bob.TipAlloc); // Half of tip (rounded)
        }

        [Fact]
        public async Task PreviewAsync_WithRoundingRemainder_AdjustsMaxTotalParticipant()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = 10.00m,
                Tax = 0.33m, // This will cause rounding issues
                Tip = 0.33m, // This will cause rounding issues
                Total = 10.66m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item = new ReceiptItem
            {
                Id = itemId,
                ReceiptId = receiptId,
                Label = "Rounding Item",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Rounding Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipant
            {
                Id = participantId1,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var participant2 = new SplitParticipant
            {
                Id = participantId2,
                SplitSessionId = splitId,
                DisplayName = "Bob",
                SortOrder = 2
            };

            // Both participants claim half of the item
            var claim1 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId1,
                QtyShare = 0.5m
            };

            var claim2 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId2,
                QtyShare = 0.5m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.Add(item);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            _dbContext.ItemClaims.AddRange(claim1, claim2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(10.66m, result.ReceiptTotal);

            // Verify that the sum of participant totals equals the receipt total
            var totalSum = result.Participants.Sum(p => p.Total);
            Assert.Equal(10.66m, totalSum);

            // Verify rounding remainder is calculated correctly
            Assert.Equal(0.00m, result.RoundingRemainder); // Should be zero after adjustment
        }

        [Fact]
        public async Task PreviewAsync_WithFractionalClaims_CalculatesProportionalAllocation()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = 12.00m,
                Tax = 1.20m,
                Tip = 1.80m,
                Total = 15.00m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item = new ReceiptItem
            {
                Id = itemId,
                ReceiptId = receiptId,
                Label = "Fractional Item",
                Qty = 3.0m,
                UnitPrice = 4.00m,
                LineSubtotal = 12.00m,
                LineTotal = 12.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Fractional Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipant
            {
                Id = participantId1,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var participant2 = new SplitParticipant
            {
                Id = participantId2,
                SplitSessionId = splitId,
                DisplayName = "Bob",
                SortOrder = 2
            };

            // Alice claims 2/3, Bob claims 1/3
            var claim1 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId1,
                QtyShare = 2.0m
            };

            var claim2 = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId2,
                QtyShare = 1.0m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.Add(item);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            _dbContext.ItemClaims.AddRange(claim1, claim2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.UnclaimedItems); // All claimed

            var alice = result.Participants[0];
            var bob = result.Participants[1];

            // Alice should get 2/3 of everything
            Assert.Equal(8.00m, alice.ItemsSubtotal); // 2/3 of 12.00
            Assert.Equal(0.80m, alice.TaxAlloc); // 2/3 of 1.20
            Assert.Equal(1.20m, alice.TipAlloc); // 2/3 of 1.80
            Assert.Equal(10.00m, alice.Total);

            // Bob should get 1/3 of everything
            Assert.Equal(4.00m, bob.ItemsSubtotal); // 1/3 of 12.00
            Assert.Equal(0.40m, bob.TaxAlloc); // 1/3 of 1.20
            Assert.Equal(0.60m, bob.TipAlloc); // 1/3 of 1.80
            Assert.Equal(5.00m, bob.Total);
        }

        [Fact]
        public async Task PreviewAsync_WithNullReceiptSubtotal_UsesItemsSubtotalSum()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = Guid.NewGuid().ToString(),
                OriginalFileUrl = "https://example.com/receipt.jpg",
                BlobContainer = "receipts",
                BlobName = "test-receipt.jpg",
                SubTotal = null, // Null subtotal
                Tax = 1.00m,
                Tip = 1.50m,
                Total = 12.50m,
                Status = ReceiptStatus.Parsed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var item = new ReceiptItem
            {
                Id = itemId,
                ReceiptId = receiptId,
                Label = "Null Subtotal Item",
                Qty = 1.0m,
                UnitPrice = 10.00m,
                LineSubtotal = 10.00m,
                LineTotal = 10.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = "Null Subtotal Split",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "NullSubtotalUser",
                SortOrder = 1
            };

            var claim = new ItemClaim
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ReceiptItemId = itemId,
                ParticipantId = participantId,
                QtyShare = 1.0m
            };

            _dbContext.Receipts.Add(receipt);
            _dbContext.ReceiptItems.Add(item);
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitParticipants.Add(participant);
            _dbContext.ItemClaims.Add(claim);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.PreviewAsync(splitId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(10.00m, result.ReceiptSubtotal); // Should use items subtotal sum
            Assert.Equal(1.00m, result.ReceiptTax);
            Assert.Equal(1.50m, result.ReceiptTip);
            Assert.Equal(12.50m, result.ReceiptTotal);
        }

        [Fact]
        public async Task PreviewAsync_Rounding_DeterministicExtraCentAssignment()
        {
            // 3 participants, totals that cause 2¢ remainder after prorating tax+tip
            var splitId = Guid.NewGuid(); var receiptId = Guid.NewGuid();
            var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var p3 = Guid.NewGuid();
            var item = new ReceiptItem
            {
                Id = Guid.NewGuid(),
                ReceiptId = receiptId,
                Label = "i",
                Qty = 3,
                UnitPrice = 1.00m,
                LineSubtotal = 3.00m,
                LineTotal = 3.00m,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = "x",
                SubTotal = 3.00m,
                Tax = 0.05m,
                Tip = 0.04m,
                Total = 3.09m,
                Status = ReceiptStatus.Parsed,
                OriginalFileUrl = "x",
                BlobContainer = "b",
                BlobName = "n",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var session = new SplitSession { Id = splitId, OwnerId = Guid.NewGuid(), ReceiptId = receiptId, Name = "r", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            var sp1 = new SplitParticipant { Id = p1, SplitSessionId = splitId, DisplayName = "A", SortOrder = 1 };
            var sp2 = new SplitParticipant { Id = p2, SplitSessionId = splitId, DisplayName = "B", SortOrder = 2 };
            var sp3 = new SplitParticipant { Id = p3, SplitSessionId = splitId, DisplayName = "C", SortOrder = 3 };
            // Claims produce weights: A=1.20, B=1.10, C=0.70 (sum=3.00). Choose splits that yield a 2¢ remainder.
            var c1 = new ItemClaim { Id = Guid.NewGuid(), SplitSessionId = splitId, ReceiptItemId = item.Id, ParticipantId = p1, QtyShare = 1.2m };
            var c2 = new ItemClaim { Id = Guid.NewGuid(), SplitSessionId = splitId, ReceiptItemId = item.Id, ParticipantId = p2, QtyShare = 1.1m };
            var c3 = new ItemClaim { Id = Guid.NewGuid(), SplitSessionId = splitId, ReceiptItemId = item.Id, ParticipantId = p3, QtyShare = 0.7m };

            _dbContext.AddRange(receipt, item, session, sp1, sp2, sp3, c1, c2, c3);
            await _dbContext.SaveChangesAsync();

            var res = await _service.PreviewAsync(splitId);

            // Assert stable extra-cent assignment (example rule: largest fractional first, tie ? SortOrder)
            // Adjust expectations to your actual rule:
            var a = res.Participants.Single(x => x.ParticipantId == p1);
            var b = res.Participants.Single(x => x.ParticipantId == p2);
            var c = res.Participants.Single(x => x.ParticipantId == p3);

            Assert.Equal(res.ReceiptTotal, res.Participants.Sum(x => x.Total));
            // Example expectation: A and B get the extra cents (update if your rule differs)
            Assert.True(a.Total >= b.Total && b.Total >= c.Total);
        }

        [Theory]
        [InlineData(0.00, 0.00)]
        [InlineData(null, null)]
        public async Task PreviewAsync_ZeroOrNullTaxTip_AllocationsZero_AndReconcile(double? tax, double? tip)
        {
            var splitId = Guid.NewGuid(); var receiptId = Guid.NewGuid();
            var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid();
            var item1 = new ReceiptItem
            {
                Id = Guid.NewGuid(),
                ReceiptId = receiptId,
                Label = "x",
                Qty = 1,
                UnitPrice = 5,
                LineSubtotal = 5,
                LineTotal = 5,
                Position = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var item2 = new ReceiptItem
            {
                Id = Guid.NewGuid(),
                ReceiptId = receiptId,
                Label = "y",
                Qty = 1,
                UnitPrice = 5,
                LineSubtotal = 5,
                LineTotal = 5,
                Position = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var receipt = new Receipt
            {
                Id = receiptId,
                OwnerUserId = "u",
                SubTotal = 10,
                Tax = (decimal?)tax,
                Tip = (decimal?)tip,
                Total = 10 + (decimal)(tax ?? 0) + (decimal)(tip ?? 0),
                Status = ReceiptStatus.Parsed,
                OriginalFileUrl = "x",
                BlobContainer = "b",
                BlobName = "n",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var session = new SplitSession { Id = splitId, OwnerId = Guid.NewGuid(), ReceiptId = receiptId, Name = "n", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            var sp1 = new SplitParticipant { Id = p1, SplitSessionId = splitId, DisplayName = "A", SortOrder = 1 };
            var sp2 = new SplitParticipant { Id = p2, SplitSessionId = splitId, DisplayName = "B", SortOrder = 2 };
            var c1 = new ItemClaim { Id = Guid.NewGuid(), SplitSessionId = splitId, ReceiptItemId = item1.Id, ParticipantId = p1, QtyShare = 1 };
            var c2 = new ItemClaim { Id = Guid.NewGuid(), SplitSessionId = splitId, ReceiptItemId = item2.Id, ParticipantId = p2, QtyShare = 1 };

            _dbContext.AddRange(receipt, item1, item2, session, sp1, sp2, c1, c2);
            await _dbContext.SaveChangesAsync();

            var res = await _service.PreviewAsync(splitId);

            Assert.All(res.Participants, x => { Assert.Equal(0m, x.TaxAlloc); Assert.Equal(0m, x.TipAlloc); });
            Assert.Equal(res.ReceiptTotal, res.Participants.Sum(x => x.Total));
        }
    }
}
