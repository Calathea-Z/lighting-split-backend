using Api.Data;
using Api.Models.Splits;
using Api.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests
{
    public class ShareCodeServiceTests : IDisposable
    {
        private readonly LightningDbContext _dbContext;
        private readonly ShareCodeService _service;

        public ShareCodeServiceTests()
        {
            // Create in-memory database for testing
            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _service = new ShareCodeService(_dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldReturnCodeOfCorrectLength()
        {
            // Arrange
            var expectedLength = 8;

            // Act
            var result = await _service.GenerateUniqueAsync(expectedLength);

            // Assert
            Assert.Equal(expectedLength, result.Length);
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldReturnCodeOfCustomLength()
        {
            // Arrange
            var expectedLength = 12;

            // Act
            var result = await _service.GenerateUniqueAsync(expectedLength);

            // Assert
            Assert.Equal(expectedLength, result.Length);
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldReturnCodeWithValidCharacters()
        {
            // Arrange
            var validAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            // Act
            var result = await _service.GenerateUniqueAsync(8);

            // Assert
            Assert.All(result, c => Assert.Contains(c, validAlphabet));
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldNotContainExcludedCharacters()
        {
            // Arrange
            var excludedCharacters = "O0I1";

            // Act
            var result = await _service.GenerateUniqueAsync(8);

            // Assert
            Assert.DoesNotContain(result, c => excludedCharacters.Contains(c));
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldRetryWhenCodeExists()
        {
            // Arrange - Create a split session with a specific share code
            var existingCode = "TESTCODE";
            var splitSession = new SplitSession
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                ReceiptId = Guid.NewGuid(),
                ShareCode = existingCode,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.SplitSessions.Add(splitSession);
            await _dbContext.SaveChangesAsync();

            // Act - Generate a code (should be different from existing)
            var result = await _service.GenerateUniqueAsync(8);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(8, result.Length);
            Assert.NotEqual(existingCode, result);
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _service.GenerateUniqueAsync(8, cts.Token));
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldGenerateDifferentCodesOnMultipleCalls()
        {
            // Act
            var result1 = await _service.GenerateUniqueAsync(8);
            var result2 = await _service.GenerateUniqueAsync(8);

            // Assert
            Assert.NotEqual(result1, result2);
        }


        [Fact]
        public async Task GenerateUniqueAsync_ShouldUseCorrectAlphabetDistribution()
        {
            // Arrange
            var validAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            // Act
            var results = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                results.Add(await _service.GenerateUniqueAsync(8));
            }

            // Assert
            var allCharacters = string.Join("", results);
            var uniqueCharacters = allCharacters.Distinct().ToArray();

            // All characters should be from the valid alphabet
            Assert.All(uniqueCharacters, c => Assert.Contains(c, validAlphabet));

            // Should have reasonable distribution (not all the same character)
            var characterCounts = allCharacters.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            Assert.True(characterCounts.Count > 1, "Should generate codes with multiple different characters");
        }

        [Fact]
        public async Task GenerateUniqueAsync_ShouldGenerateUniqueCodesEvenWithManyExisting()
        {
            // Arrange - Add many existing split sessions
            var existingCodes = new List<string>();
            for (int i = 0; i < 50; i++)
            {
                var splitSession = new SplitSession
                {
                    Id = Guid.NewGuid(),
                    OwnerId = Guid.NewGuid(),
                    ReceiptId = Guid.NewGuid(),
                    ShareCode = $"EXIST{i:D3}",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.SplitSessions.Add(splitSession);
                existingCodes.Add(splitSession.ShareCode);
            }
            await _dbContext.SaveChangesAsync();

            // Act - Generate new codes
            var newCodes = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                newCodes.Add(await _service.GenerateUniqueAsync(8));
            }

            // Assert - All new codes should be unique and not match existing ones
            Assert.Equal(10, newCodes.Distinct().Count()); // All unique
            Assert.All(newCodes, code => Assert.DoesNotContain(code, existingCodes));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GenerateUniqueAsync_InvalidLength_Throws(int len)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.GenerateUniqueAsync(len));
        }

        [Fact]
        public async Task GenerateUniqueAsync_AllUppercase()
        {
            var code = await _service.GenerateUniqueAsync(10);
            Assert.Equal(code, code.ToUpperInvariant());
        }

        [Fact]
        public async Task GenerateUniqueAsync_Parallel_IsUnique()
        {
            var tasks = Enumerable.Range(0, 50).Select(_ => _service.GenerateUniqueAsync(8));
            var codes = await Task.WhenAll(tasks);
            Assert.Equal(codes.Length, codes.Distinct().Count());
        }
    }

}
