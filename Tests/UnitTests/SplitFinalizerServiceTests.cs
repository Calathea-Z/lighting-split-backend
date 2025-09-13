using Api.Contracts.Payment;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Models;
using Api.Models.Owners;
using Api.Models.Splits;
using Api.Services.Payments;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests
{
    public class SplitFinalizerServiceTests : IDisposable
    {
        private readonly LightningDbContext _dbContext;
        private readonly Mock<ISplitCalculator> _mockCalculator;
        private readonly Mock<IPaymentLinkBuilder> _mockPaymentLinkBuilder;
        private readonly SplitFinalizerService _service;

        public SplitFinalizerServiceTests()
        {
            // Create in-memory database for testing
            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _mockCalculator = new Mock<ISplitCalculator>();
            _mockPaymentLinkBuilder = new Mock<IPaymentLinkBuilder>();
            _service = new SplitFinalizerService(_dbContext, _mockCalculator.Object, _mockPaymentLinkBuilder.Object);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [Fact]
        public async Task FinalizeAsync_WithValidSplit_FinalizesAndReturnsResponse()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Test Split",
                IsFinalized = false,
                ShareCode = null,
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

            var preview = new SplitPreviewDto(
                splitId,
                20.00m,
                2.00m,
                3.00m,
                25.00m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId1, "Alice", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m),
                    new(participantId2, "Bob", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/alice", false, null),
                new(Guid.NewGuid(), "cashapp", "Cash App", "Cash App", "https://cash.app/alice", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.AddRange(participant1, participant2);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(splitId, result.SplitId);
            Assert.NotNull(result.ShareCode);
            Assert.Equal(8, result.ShareCode.Length);
            Assert.Equal($"{baseUrl}/s/{result.ShareCode}", result.ShareUrl);
            Assert.Equal(2, result.Participants.Count);

            // Verify split was finalized
            var updatedSplit = await _dbContext.SplitSessions.FindAsync(splitId);
            Assert.True(updatedSplit!.IsFinalized);
            Assert.NotNull(updatedSplit.ShareCode);
            Assert.NotNull(updatedSplit.FinalizedAt);

            // Verify snapshot was created
            var snapshot = await _dbContext.SplitResults
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.SplitSessionId == splitId);
            Assert.NotNull(snapshot);
            Assert.Equal(2, snapshot.Participants.Count);

            // Verify calculator was called
            _mockCalculator.Verify(x => x.PreviewAsync(splitId), Times.Once);
        }

        [Fact]
        public async Task FinalizeAsync_WithNonExistentSplit_ThrowsKeyNotFoundException()
        {
            // Arrange
            var nonExistentSplitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var baseUrl = "https://example.com";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _service.FinalizeAsync(nonExistentSplitId, ownerId, baseUrl));
            Assert.Equal("Split not found.", exception.Message);
        }

        [Fact]
        public async Task FinalizeAsync_WithWrongOwner_ThrowsKeyNotFoundException()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var wrongOwnerId = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Test Split",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.SplitSessions.Add(split);
            await _dbContext.SaveChangesAsync();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _service.FinalizeAsync(splitId, wrongOwnerId, baseUrl));
            Assert.Equal("Split not found.", exception.Message);
        }

        [Fact]
        public async Task FinalizeAsync_AlreadyFinalized_ReturnsExistingResponse()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var baseUrl = "https://example.com";
            var existingShareCode = "EXISTING";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Test Split",
                IsFinalized = true,
                ShareCode = existingShareCode,
                FinalizedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var splitResult = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var participantResult = new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                SplitResultId = splitResult.Id,
                ParticipantId = participantId,
                DisplayName = "Alice",
                ItemsSubtotal = 10.00m,
                DiscountAlloc = 0.00m,
                TaxAlloc = 1.00m,
                TipAlloc = 1.50m,
                Total = 12.50m
            };

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/alice", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(participant);
            _dbContext.SplitResults.Add(splitResult);
            _dbContext.SplitParticipantResults.Add(participantResult);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(splitId, result.SplitId);
            Assert.Equal(existingShareCode, result.ShareCode);
            Assert.Equal($"{baseUrl}/s/{existingShareCode}", result.ShareUrl);
            Assert.Single(result.Participants);

            // Verify calculator was NOT called (idempotent behavior)
            _mockCalculator.Verify(x => x.PreviewAsync(splitId), Times.Never);
        }

        [Fact]
        public async Task FinalizeAsync_GeneratesUniqueShareCode()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Test Split",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var preview = new SplitPreviewDto(
                splitId,
                10.00m,
                1.00m,
                1.50m,
                12.50m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId, "Alice", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/alice", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(participant);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result.ShareCode);
            Assert.Equal(8, result.ShareCode.Length);

            // Verify share code contains only valid characters
            var validChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            Assert.True(result.ShareCode.All(c => validChars.Contains(c)));

            // Verify share code is unique in database
            var existingCodes = await _dbContext.SplitSessions
                .Where(s => s.ShareCode == result.ShareCode)
                .CountAsync();
            Assert.Equal(1, existingCodes);
        }

        [Fact]
        public async Task FinalizeAsync_WithMultipleParticipants_BuildsCorrectResponse()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var participantId3 = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Multi Participant Split",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participants = new List<SplitParticipant>
            {
                new() { Id = participantId1, SplitSessionId = splitId, DisplayName = "Charlie", SortOrder = 3 },
                new() { Id = participantId2, SplitSessionId = splitId, DisplayName = "Alice", SortOrder = 1 },
                new() { Id = participantId3, SplitSessionId = splitId, DisplayName = "Bob", SortOrder = 2 }
            };

            var preview = new SplitPreviewDto(
                splitId,
                30.00m,
                3.00m,
                4.50m,
                37.50m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId1, "Charlie", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m),
                    new(participantId2, "Alice", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m),
                    new(participantId3, "Bob", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/user", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.AddRange(participants);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Participants.Count);

            // Verify participants are ordered by display name (as per service logic)
            var participantNames = result.Participants.Select(p => p.DisplayName).ToList();
            Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, participantNames);

            // Verify each participant has correct data
            foreach (var participant in result.Participants)
            {
                Assert.Equal(12.50m, participant.Total);
                Assert.Single(participant.PaymentLinks);
                Assert.Equal("venmo", participant.PaymentLinks[0].PlatformKey);
            }
        }

        [Fact]
        public async Task FinalizeAsync_WithPayoutMethods_CallsPaymentLinkBuilderCorrectly()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Test Split",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var platform = new PayoutPlatform
            {
                Id = 1,
                Key = "venmo",
                DisplayName = "Venmo",
                SortOrder = 10
            };

            var payoutMethod = new OwnerPayoutMethod
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                PlatformId = 1,
                Platform = platform,
                HandleOrUrl = "alice-venmo",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var preview = new SplitPreviewDto(
                splitId,
                10.00m,
                1.00m,
                1.50m,
                12.50m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId, "Alice", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/alice", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(participant);
            _dbContext.PayoutPlatforms.Add(platform);
            _dbContext.OwnerPayoutMethods.Add(payoutMethod);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result);

            // Verify PaymentLinkBuilder was called with correct parameters
            _mockPaymentLinkBuilder.Verify(
                x => x.BuildManyAsync(
                    It.Is<IEnumerable<OwnerPayoutMethod>>(methods =>
                        methods.Count() == 1 &&
                        methods.First().Id == payoutMethod.Id),
                    It.Is<decimal>(amount => amount == 12.50m),
                    It.Is<string>(note => note == $"Lightning Split {splitId.ToString().Substring(0, 8)}")
                ),
                Times.Once);
        }

        [Fact]
        public async Task FinalizeAsync_CreatesCorrectSnapshot()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId1 = Guid.NewGuid();
            var participantId2 = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Snapshot Test",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participants = new List<SplitParticipant>
            {
                new() { Id = participantId1, SplitSessionId = splitId, DisplayName = "Alice", SortOrder = 1 },
                new() { Id = participantId2, SplitSessionId = splitId, DisplayName = "Bob", SortOrder = 2 }
            };

            var preview = new SplitPreviewDto(
                splitId,
                20.00m,
                2.00m,
                3.00m,
                25.00m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId1, "Alice", 10.00m, -1.00m, 1.00m, 1.50m, 11.50m),
                    new(participantId2, "Bob", 10.00m, 1.00m, 1.00m, 1.50m, 13.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            var paymentLinks = new List<PaymentLink>
            {
                new(Guid.NewGuid(), "venmo", "Venmo", "Venmo", "https://venmo.com/user", false, null)
            };

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.AddRange(participants);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(paymentLinks);

            // Act
            await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            var snapshot = await _dbContext.SplitResults
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.SplitSessionId == splitId);

            Assert.NotNull(snapshot);
            Assert.Equal(splitId, snapshot.SplitSessionId);
            Assert.Equal(2, snapshot.Participants.Count);

            var aliceResult = snapshot.Participants.First(p => p.ParticipantId == participantId1);
            Assert.Equal("Alice", aliceResult.DisplayName);
            Assert.Equal(10.00m, aliceResult.ItemsSubtotal);
            Assert.Equal(-1.00m, aliceResult.DiscountAlloc);
            Assert.Equal(1.00m, aliceResult.TaxAlloc);
            Assert.Equal(1.50m, aliceResult.TipAlloc);
            Assert.Equal(11.50m, aliceResult.Total);

            var bobResult = snapshot.Participants.First(p => p.ParticipantId == participantId2);
            Assert.Equal("Bob", bobResult.DisplayName);
            Assert.Equal(10.00m, bobResult.ItemsSubtotal);
            Assert.Equal(1.00m, bobResult.DiscountAlloc);
            Assert.Equal(1.00m, bobResult.TaxAlloc);
            Assert.Equal(1.50m, bobResult.TipAlloc);
            Assert.Equal(13.50m, bobResult.Total);
        }

        [Fact]
        public async Task FinalizeAsync_WithEmptyPayoutMethods_StillWorks()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();
            var baseUrl = "https://example.com";

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "No Payout Methods",
                IsFinalized = false,
                ShareCode = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var participant = new SplitParticipant
            {
                Id = participantId,
                SplitSessionId = splitId,
                DisplayName = "Alice",
                SortOrder = 1
            };

            var preview = new SplitPreviewDto(
                splitId,
                10.00m,
                1.00m,
                1.50m,
                12.50m,
                new List<SplitParticipantTotalDto>
                {
                    new(participantId, "Alice", 10.00m, 0.00m, 1.00m, 1.50m, 12.50m)
                },
                new List<SplitUnclaimedItemDto>(),
                0.00m
            );

            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(participant);
            await _dbContext.SaveChangesAsync();

            _mockCalculator
                .Setup(x => x.PreviewAsync(splitId))
                .ReturnsAsync(preview);

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>()); // Empty payment links

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Participants);
            Assert.Empty(result.Participants[0].PaymentLinks);
        }

        [Theory]
        [InlineData("https://ex.com", "https://ex.com/s/")]
        [InlineData("https://ex.com/", "https://ex.com/s/")]
        public async Task FinalizeAsync_BaseUrl_Normalized(string baseUrl, string expectedPrefix)
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var participantId = Guid.NewGuid();

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Base URL",
                IsFinalized = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(new SplitParticipant { Id = participantId, SplitSessionId = splitId, DisplayName = "A", SortOrder = 1 });
            await _dbContext.SaveChangesAsync();

            var preview = new SplitPreviewDto(
                splitId, 10m, 1m, 1.5m, 12.5m,
                new List<SplitParticipantTotalDto> { new(participantId, "A", 10m, 0m, 1m, 1.5m, 12.5m) },
                new List<SplitUnclaimedItemDto>(), 0m
            );

            _mockCalculator.Setup(x => x.PreviewAsync(splitId)).ReturnsAsync(preview);
            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, baseUrl);

            // Assert
            Assert.StartsWith(expectedPrefix, result.ShareUrl);
        }

        [Fact]
        public async Task FinalizeAsync_AlreadyFinalized_PicksLatestSnapshot()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var shareCode = "EXISTINGX";
            var pIdOld = Guid.NewGuid();
            var pIdNew = Guid.NewGuid();

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Finalized",
                IsFinalized = true,
                ShareCode = shareCode,
                FinalizedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.SplitSessions.Add(split);

            var old = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Participants = new List<SplitParticipantResult>
        {
            new() { Id = Guid.NewGuid(), SplitResultId = Guid.Empty, ParticipantId = pIdOld, DisplayName = "Old", ItemsSubtotal = 1, DiscountAlloc=0, TaxAlloc=0, TipAlloc=0, Total=1 }
        }
            };
            var newer = new SplitResult
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                CreatedAt = DateTimeOffset.UtcNow,
                Participants = new List<SplitParticipantResult>
        {
            new() { Id = Guid.NewGuid(), SplitResultId = Guid.Empty, ParticipantId = pIdNew, DisplayName = "New", ItemsSubtotal = 2, DiscountAlloc=0, TaxAlloc=0, TipAlloc=0, Total=2 }
        }
            };
            _dbContext.SplitResults.AddRange(old, newer);
            await _dbContext.SaveChangesAsync();

            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var result = await _service.FinalizeAsync(splitId, ownerId, "https://ex.com");

            // Assert
            Assert.Equal(shareCode, result.ShareCode);
            Assert.Contains(result.Participants, p => p.DisplayName == "New");
            Assert.DoesNotContain(result.Participants, p => p.DisplayName == "Old");
            _mockCalculator.Verify(x => x.PreviewAsync(It.IsAny<Guid>()), Times.Never); // idempotent path
        }

        [Fact]
        public async Task FinalizeAsync_Concurrent_SameCode_AndSingleSnapshot()
        {
            // Arrange
            var splitId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var pId = Guid.NewGuid();

            var split = new SplitSession
            {
                Id = splitId,
                OwnerId = ownerId,
                ReceiptId = Guid.NewGuid(),
                Name = "Concurrent",
                IsFinalized = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.SplitSessions.Add(split);
            _dbContext.SplitParticipants.Add(new SplitParticipant { Id = pId, SplitSessionId = splitId, DisplayName = "A", SortOrder = 1 });
            await _dbContext.SaveChangesAsync();

            var preview = new SplitPreviewDto(
                splitId, 10m, 1m, 1.5m, 12.5m,
                new List<SplitParticipantTotalDto> { new(pId, "A", 10m, 0m, 1m, 1.5m, 12.5m) },
                new List<SplitUnclaimedItemDto>(), 0m
            );
            _mockCalculator.Setup(x => x.PreviewAsync(splitId)).ReturnsAsync(preview);
            _mockPaymentLinkBuilder
                .Setup(x => x.BuildManyAsync(It.IsAny<IEnumerable<OwnerPayoutMethod>>(), It.IsAny<decimal>(), It.IsAny<string>()))
                .ReturnsAsync(new List<PaymentLink>());

            // Act
            var t1 = _service.FinalizeAsync(splitId, ownerId, "https://ex.com");
            var t2 = _service.FinalizeAsync(splitId, ownerId, "https://ex.com");
            var results = await Task.WhenAll(t1, t2);

            // Assert
            Assert.Equal(results[0].ShareCode, results[1].ShareCode);
            var snapshots = await _dbContext.SplitResults.CountAsync(r => r.SplitSessionId == splitId);
            Assert.Equal(1, snapshots); // if this flakes, consider a DB constraint or lock strategy
        }



    }
}
