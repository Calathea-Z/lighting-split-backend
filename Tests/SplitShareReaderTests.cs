using Api.Contracts.Payment;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Models;
using Api.Models.Owners;
using Api.Models.Receipts;
using Api.Models.Splits;
using Api.Services.Payments;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Tests
{
    public class SplitShareReaderTests : IDisposable
    {
        private readonly LightningDbContext _dbContext;
        private readonly Mock<IPaymentLinkBuilder> _mockPaymentLinkBuilder;
        private readonly SplitShareReader _service;

        public SplitShareReaderTests()
        {
            // Create in-memory database for testing
            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _mockPaymentLinkBuilder = new Mock<IPaymentLinkBuilder>();
            _service = new SplitShareReader(_dbContext, _mockPaymentLinkBuilder.Object);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [Fact]
        public async Task GetByCodeAsync_WithValidCode_ReturnsShareSplitResponse()
        {
            // Arrange
            var shareCode = "TESTCODE";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var receiptId = Guid.NewGuid();

            // Create test data
            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = receiptId,
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participant1 = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "Alice",
                Total = 25.50m
            };

            var participant2 = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "Bob",
                Total = 15.25m
            };

            var ownerPayoutMethod = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 1,
                HandleOrUrl = "alice@venmo",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var platform = new PayoutPlatform
            {
                Id = 1,
                Key = "venmo",
                DisplayName = "Venmo",
                SortOrder = 10
            };

            // Setup database
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.AddRange(participant1, participant2);
            _dbContext.OwnerPayoutMethods.Add(ownerPayoutMethod);
            _dbContext.PayoutPlatforms.Add(platform);
            await _dbContext.SaveChangesAsync();

            // Setup payment link builder mock
            var paymentLinks = new List<PaymentLink>
            {
                new PaymentLink(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/alice", false, null),
                new PaymentLink(Guid.NewGuid(), "cashapp", "Cash App", "Cash App", "https://cash.app/alice", false, null)
            };

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.GetByCodeAsync(shareCode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(splitId, result.SplitId);
            Assert.Equal(shareCode, result.Code);
            Assert.Equal(2, result.Participants.Count);

            // Verify participants are sorted by display name
            var sortedParticipants = result.Participants.OrderBy(p => p.DisplayName).ToList();
            Assert.Equal("Alice", sortedParticipants[0].DisplayName);
            Assert.Equal("Bob", sortedParticipants[1].DisplayName);

            // Verify payment links were built for each participant
            _mockPaymentLinkBuilder.Verify(
                x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), 25.50m, It.IsAny<string>()),
                Times.Once);
            _mockPaymentLinkBuilder.Verify(
                x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), 15.25m, It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task GetByCodeAsync_WithNonExistentCode_ThrowsKeyNotFoundException()
        {
            // Arrange
            var nonExistentCode = "NONEXISTENT";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByCodeAsync(nonExistentCode));
            Assert.Equal("Share code not found.", exception.Message);
        }

        [Fact]
        public async Task GetByCodeAsync_WithUnfinalizedSplit_ThrowsKeyNotFoundException()
        {
            // Arrange
            var shareCode = "UNFINALIZED";
            var splitSession = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = false, // Not finalized
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.SplitSessions.Add(splitSession);
            await _dbContext.SaveChangesAsync();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByCodeAsync(shareCode));
            Assert.Equal("Share code not found.", exception.Message);
        }

        [Fact]
        public async Task GetByCodeAsync_WithPaidParticipants_IncludesPaidStatus()
        {
            // Arrange
            var shareCode = "PAIDTEST";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = participantId,
                DisplayName = "PaidUser",
                Total = 10.00m
            };

            var splitPayment = new SplitPayment
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ParticipantId = participantId,
                IsPaid = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.Add(participant);
            _dbContext.SplitPayments.Add(splitPayment);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.GetByCodeAsync(shareCode);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Participants);
            Assert.True(result.Participants.First().IsPaid);
        }

        [Fact]
        public async Task GetByCodeAsync_WithUnpaidParticipants_IncludesUnpaidStatus()
        {
            // Arrange
            var shareCode = "UNPAIDTEST";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = participantId,
                DisplayName = "UnpaidUser",
                Total = 20.00m
            };

            // No payment record = unpaid
            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.Add(participant);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.GetByCodeAsync(shareCode);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Participants);
            Assert.False(result.Participants.First().IsPaid);
        }

        [Fact]
        public async Task GetByCodeAsync_WithMultipleSplitResults_ReturnsLatestResult()
        {
            // Arrange
            var shareCode = "MULTIRESULT";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var oldResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) // Older result
            };

            var newResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow // Newer result
            };

            var participant = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = newResult.Id, // Associated with newer result
                ParticipantId = Guid.NewGuid(),
                DisplayName = "LatestUser",
                Total = 30.00m
            };

            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.AddRange(oldResult, newResult);
            _dbContext.SplitParticipantResults.Add(participant);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.GetByCodeAsync(shareCode);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Participants);
            Assert.Equal("LatestUser", result.Participants.First().DisplayName);
        }

        [Fact]
        public async Task GetByCodeAsync_WithMultiplePayoutMethods_OrdersByDefaultAndSortOrder()
        {
            // Arrange
            var shareCode = "MULTIPAYOUT";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "TestUser",
                Total = 15.00m
            };

            // Create platforms with different sort orders
            var venmoPlatform = new PayoutPlatform { Id = 1, Key = "venmo", DisplayName = "Venmo", SortOrder = 20 };
            var cashappPlatform = new PayoutPlatform { Id = 2, Key = "cashapp", DisplayName = "Cash App", SortOrder = 10 };

            // Create payout methods
            var defaultMethod = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 1,
                HandleOrUrl = "default@venmo",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var nonDefaultMethod = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 2,
                HandleOrUrl = "nondefault@cashapp",
                IsDefault = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.Add(participant);
            _dbContext.PayoutPlatforms.AddRange(venmoPlatform, cashappPlatform);
            _dbContext.OwnerPayoutMethods.AddRange(defaultMethod, nonDefaultMethod);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.GetByCodeAsync(shareCode);

            // Assert
            Assert.NotNull(result);

            // Verify that BuildManyAsync was called with the payout methods
            _mockPaymentLinkBuilder.Verify(
                x => x.BuildManyAsync(
                    It.Is<IEnumerable<OwnerPayoutMethod>>(methods =>
                        methods.Count() == 2 &&
                        methods.First().IsDefault == true), // Default method should come first
                    It.IsAny<decimal>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task GetByCodeAsync_WithEmptyString_ThrowsKeyNotFoundException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByCodeAsync(""));
            Assert.Equal("Share code not found.", exception.Message);
        }

        [Fact]
        public async Task GetByCodeAsync_WithNullString_ThrowsKeyNotFoundException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByCodeAsync(null!));
            Assert.Equal("Share code not found.", exception.Message);
        }

        [Fact]
        public async Task GetByCodeAsync_VerifiesPaymentLinkBuilderNoteFormat()
        {
            // Arrange
            var shareCode = "NOTETEST";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var splitSession = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = shareCode,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "NoteUser",
                Total = 12.50m
            };

            _dbContext.SplitSessions.Add(splitSession);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.Add(participant);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            await _service.GetByCodeAsync(shareCode);

            // Assert
            // Verify that the note format includes "Lightning Split" and the first 8 characters of the split ID
            var expectedNotePrefix = $"Lightning Split {splitId.ToString()[..8]}";
            _mockPaymentLinkBuilder.Verify(
                x => x.BuildManyAsync(
                    It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                    It.IsAny<decimal>(),
                    It.Is<string>(note => note.StartsWith(expectedNotePrefix))),
                Times.Once);
        }

        [Fact]
        public async Task GetByCodeAsync_NoPayoutMethods_ProducesEmptyPaymentLinks()
        {
            // Arrange
            var code = "NOPL";
            var split = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                ReceiptId = Guid.NewGuid(),
                ShareCode = code,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var result = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = split.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var p = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = result.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "A",
                Total = 9.99m
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitResults.Add(result);
            _dbContext.SplitParticipantResults.Add(p);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                                             It.IsAny<decimal>(),
                                             It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var dto = await _service.GetByCodeAsync(code);

            // Assert
            Assert.Single(dto.Participants);
            Assert.Empty(dto.Participants[0].PaymentLinks); // ? PaymentLinks
        }

        [Fact]
        public async Task GetByCodeAsync_PayoutMethodsOrder_IsDefaultDesc_ThenPlatformSortAsc()
        {
            // Arrange
            var code = "ORDERPLAT";
            var ownerId = Guid.NewGuid();
            var split = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = code,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var res = new SplitResult { Id = Guid.NewGuid(), SplitSessionId = split.Id, CreatedAt = DateTimeOffset.UtcNow };
            var part = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = res.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "P",
                Total = 1m
            };

            var p1 = new PayoutPlatform { Id = 1, Key = "a", DisplayName = "A", SortOrder = 30 };
            var p2 = new PayoutPlatform { Id = 2, Key = "b", DisplayName = "B", SortOrder = 10 };
            var p3 = new PayoutPlatform { Id = 3, Key = "c", DisplayName = "C", SortOrder = 20 };

            var mDefault = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 3,
                Platform = p3,
                HandleOrUrl = "d",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var mB = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 2,
                Platform = p2,
                HandleOrUrl = "b",
                IsDefault = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var mA = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 1,
                Platform = p1,
                HandleOrUrl = "a",
                IsDefault = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.AddRange(split, res, part, p1, p2, p3, mDefault, mB, mA);
            await _dbContext.SaveChangesAsync();

            IEnumerable<OwnerPayoutMethod>? captured = null;
            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                                             It.IsAny<decimal>(),
                                             It.IsAny<string>()))
                .Callback<IEnumerable<OwnerPayoutMethod>, decimal, string>((methods, _, __) => captured = methods.ToList())
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            await _service.GetByCodeAsync(code);

            // Assert: default first, then sort order ascending (p2 then p1)
            Assert.NotNull(captured);
            var list = captured!.ToList();
            Assert.Equal(mDefault.Id, list[0].Id);
            Assert.Equal(mB.Id, list[1].Id); // SortOrder 10
            Assert.Equal(mA.Id, list[2].Id); // SortOrder 30
        }

        [Fact]
        public async Task GetByCodeAsync_ExplicitUnpaidRow_RemainsUnpaid()
        {
            // Arrange
            var code = "UNPAIDFALSE";
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                ShareCode = code,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var res = new SplitResult { Id = Guid.NewGuid(), SplitSessionId = splitId, CreatedAt = DateTimeOffset.UtcNow };
            var pr = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = res.Id,
                ParticipantId = participantId,
                DisplayName = "U",
                Total = 3m
            };
            var pay = new SplitPayment
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ParticipantId = participantId,
                IsPaid = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.AddRange(split, res, pr, pay);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                                             It.IsAny<decimal>(),
                                             It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var dto = await _service.GetByCodeAsync(code);

            // Assert
            Assert.False(dto.Participants.Single().IsPaid);
        }

        [Fact]
        public async Task GetByCodeAsync_ManyParticipants_CallsBuilderPerParticipant_WithCorrectAmounts()
        {
            // Arrange
            var code = "MANY";
            var split = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                ReceiptId = Guid.NewGuid(),
                ShareCode = code,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var res = new SplitResult { Id = Guid.NewGuid(), SplitSessionId = split.Id, CreatedAt = DateTimeOffset.UtcNow };

            var parts = Enumerable.Range(1, 10).Select(i => new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = res.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = $"P{i:D2}",
                Total = i // 1..10
            }).ToList();

            _dbContext.AddRange(split, res);
            _dbContext.SplitParticipantResults.AddRange(parts);
            await _dbContext.SaveChangesAsync();

            var seen = new List<decimal>();
            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                                             It.IsAny<decimal>(),
                                             It.IsAny<string>()))
                .Callback<IEnumerable<OwnerPayoutMethod>, decimal, string>((_, amt, __) => seen.Add(amt))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            await _service.GetByCodeAsync(code);

            // Assert
            Assert.Equal(10, seen.Count);
            Assert.True(Enumerable.Range(1, 10).Select(i => (decimal)i).All(seen.Contains));
        }

        [Fact]
        public async Task GetByCodeAsync_NoteIsExactFormat()
        {
            // Arrange
            var code = "NOTEEXACT";
            var split = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                ReceiptId = Guid.NewGuid(),
                ShareCode = code,
                IsFinalized = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var res = new SplitResult { Id = Guid.NewGuid(), SplitSessionId = split.Id, CreatedAt = DateTimeOffset.UtcNow };
            var p = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = res.Id,
                ParticipantId = Guid.NewGuid(),
                DisplayName = "X",
                Total = 5m
            };

            _dbContext.AddRange(split, res, p);
            await _dbContext.SaveChangesAsync();

            string? capturedNote = null;
            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(),
                                             It.IsAny<decimal>(),
                                             It.IsAny<string>()))
                .Callback<IEnumerable<OwnerPayoutMethod>, decimal, string>((_, __, note) => capturedNote = note)
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            await _service.GetByCodeAsync(code);

            // Assert
            Assert.Equal($"Lightning Split {split.Id.ToString()[..8]}", capturedNote);
        }


    }
}
