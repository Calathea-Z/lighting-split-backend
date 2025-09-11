using Api.Data;
using Api.Models.Owners;
using Api.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Collections;

namespace Tests
{
    public class AokServiceTests : IDisposable
    {
        private readonly LightningDbContext _dbContext;
        private readonly AokService _service;

        public AokServiceTests()
        {
            // Create in-memory database for testing
            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _service = new AokService(_dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithValidCookie_ReturnsOwner()
        {
            // Arrange
            var token = "test-token-123";
            var expectedHash = HashToken(token);
            var ownerId = Guid.NewGuid();

            var owner = new Owner
            {
                Id = ownerId,
                KeyHash = expectedHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = null
            };

            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", token);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ownerId, result.Id);
            Assert.Equal(expectedHash, result.KeyHash);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithValidHeader_ReturnsOwner()
        {
            // Arrange
            var token = "test-token-456";
            var expectedHash = HashToken(token);
            var ownerId = Guid.NewGuid();

            var owner = new Owner
            {
                Id = ownerId,
                KeyHash = expectedHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = null
            };

            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Add("X-AOK", token);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ownerId, result.Id);
            Assert.Equal(expectedHash, result.KeyHash);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithHeaderWhenCookieExists_PrefersCookie()
        {
            // Arrange
            var cookieToken = "cookie-token";
            var headerToken = "header-token";
            var cookieHash = HashToken(cookieToken);
            var headerHash = HashToken(headerToken);
            var cookieOwnerId = Guid.NewGuid();
            var headerOwnerId = Guid.NewGuid();

            var cookieOwner = new Owner
            {
                Id = cookieOwnerId,
                KeyHash = cookieHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = null
            };

            var headerOwner = new Owner
            {
                Id = headerOwnerId,
                KeyHash = headerHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = null
            };

            _dbContext.Owners.AddRange(cookieOwner, headerOwner);
            await _dbContext.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", cookieToken);
            httpContext.Request.Headers.Add("X-AOK", headerToken);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(cookieOwnerId, result.Id); // Should prefer cookie over header
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithRevokedOwner_ReturnsNull()
        {
            // Arrange
            var token = "revoked-token";
            var expectedHash = HashToken(token);

            var owner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = expectedHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = DateTimeOffset.UtcNow // Revoked
            };

            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", token);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithNonExistentToken_ReturnsNull()
        {
            // Arrange
            var token = "non-existent-token";
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", token);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithEmptyToken_ReturnsNull()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", "");

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithWhitespaceToken_ReturnsNull()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", "   ");

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_WithNoToken_ReturnsNull()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_UpdatesLastSeenAt()
        {
            // Arrange
            var token = "test-token-update";
            var expectedHash = HashToken(token);
            var ownerId = Guid.NewGuid();
            var originalLastSeen = DateTimeOffset.UtcNow.AddDays(-5);

            var owner = new Owner
            {
                Id = ownerId,
                KeyHash = expectedHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = originalLastSeen,
                RevokedAt = null
            };

            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)httpContext.Request.Cookies).Add("aok", token);

            // Act
            var result = await _service.ResolveOwnerAsync(httpContext);

            // Assert
            Assert.NotNull(result);

            // Verify LastSeenAt was updated
            var updatedOwner = await _dbContext.Owners.FindAsync(ownerId);
            Assert.NotNull(updatedOwner);
            Assert.True(updatedOwner.LastSeenAt > originalLastSeen);
        }

        [Fact]
        public void SetAokCookie_SetsCorrectCookieOptions()
        {
            // Arrange
            var rawToken = "test-raw-token";
            var httpContext = new DefaultHttpContext();
            var response = httpContext.Response;

            // Act
            _service.SetAokCookie(response, rawToken);

            // Assert
            Assert.True(response.Headers.ContainsKey("Set-Cookie"));
            var setCookieHeader = response.Headers["Set-Cookie"].ToString();

            // Verify cookie name
            Assert.Contains("aok=", setCookieHeader);
            Assert.Contains(rawToken, setCookieHeader);

            // Verify cookie options (case-insensitive check)
            Assert.Contains("httponly", setCookieHeader.ToLowerInvariant());
            Assert.Contains("secure", setCookieHeader.ToLowerInvariant());
            Assert.Contains("samesite=lax", setCookieHeader.ToLowerInvariant());
        }

        [Fact]
        public void SetAokCookie_WithEmptyToken_StillSetsCookie()
        {
            // Arrange
            var rawToken = "";
            var httpContext = new DefaultHttpContext();
            var response = httpContext.Response;

            // Act
            _service.SetAokCookie(response, rawToken);

            // Assert
            Assert.True(response.Headers.ContainsKey("Set-Cookie"));
            var setCookieHeader = response.Headers["Set-Cookie"].ToString();
            Assert.Contains("aok=", setCookieHeader);
        }

        [Fact]
        public void SetAokCookie_WithNullToken_ThrowsException()
        {
            // Arrange
            string? rawToken = null;
            var httpContext = new DefaultHttpContext();
            var response = httpContext.Response;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _service.SetAokCookie(response, rawToken!));
        }

        [Fact]
        public void HashToken_WithVariousTokens_ProducesConsistentHashes()
        {
            // Arrange
            var tokens = new[]
            {
                "simple-token",
                "token-with-special-chars!@#$%^&*()",
                "token-with-unicode-测试",
                "very-long-token-" + new string('x', 100),
                "token-with-spaces and tabs\tand newlines\n"
            };

            foreach (var token in tokens)
            {
                // Act
                var hash1 = HashToken(token);
                var hash2 = HashToken(token);

                // Assert
                Assert.Equal(hash1, hash2);
                Assert.NotEmpty(hash1);
                Assert.DoesNotContain("=", hash1); // Should be base64url (no padding)
                Assert.DoesNotContain("+", hash1); // Should be base64url (no +)
                Assert.DoesNotContain("/", hash1); // Should be base64url (no /)
            }
        }

        [Fact]
        public void HashToken_WithDifferentTokens_ProducesDifferentHashes()
        {
            // Arrange
            var token1 = "token1";
            var token2 = "token2";

            // Act
            var hash1 = HashToken(token1);
            var hash2 = HashToken(token2);

            // Assert
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void HashToken_WithCaseSensitiveTokens_ProducesDifferentHashes()
        {
            // Arrange
            var token1 = "Token";
            var token2 = "token";

            // Act
            var hash1 = HashToken(token1);
            var hash2 = HashToken(token2);

            // Assert
            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public async Task ResolveOwnerAsync_BlankCookie_ValidHeader_UsesHeader()
        {
            // Arrange
            var headerToken = "header-ok";
            var headerHash = HashToken(headerToken);
            var headerOwner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = headerHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            _dbContext.Owners.Add(headerOwner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", ""); // blank cookie
            ctx.Request.Headers.Add("X-AOK", headerToken);

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(headerOwner.Id, result.Id);
        }

        [Fact]
        public async Task ResolveOwnerAsync_BlankHeader_ValidCookie_UsesCookie()
        {
            // Arrange
            var cookieToken = "cookie-ok";
            var cookieHash = HashToken(cookieToken);
            var cookieOwner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = cookieHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            _dbContext.Owners.Add(cookieOwner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", cookieToken);
            ctx.Request.Headers.Add("X-AOK", "   "); // whitespace header

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(cookieOwner.Id, result.Id);
        }

        [Fact]
        public async Task ResolveOwnerAsync_CookieInvalid_HeaderValid_ReturnsNull_DoesNotPivot()
        {
            // Arrange: invalid cookie token present, header token would be valid,
            // but we DO NOT silently pivot to header when a cookie exists.
            var headerToken = "header-would-resolve";
            var headerHash = HashToken(headerToken);
            var headerOwner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = headerHash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-3)
            };
            _dbContext.Owners.Add(headerOwner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", "unknown-cookie-token"); // invalid cookie present
            ctx.Request.Headers.Add("X-AOK", headerToken); // would match, but should be ignored due to cookie presence

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert: strict cookie precedence when cookie is present but invalid → no fallback
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveOwnerAsync_BothTokensSameOwner_StillUsesCookiePath()
        {
            // Arrange
            var token = "same-token";
            var hash = HashToken(token);
            var owner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-5)
            };
            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", token);
            ctx.Request.Headers.Add("X-AOK", token);

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(owner.Id, result.Id);
        }

        [Fact]
        public async Task ResolveOwnerAsync_HeaderLowercase_Works()
        {
            // Arrange
            var token = "lowercase-header";
            var hash = HashToken(token);
            var owner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Add("x-aok", token); // lower-case header name

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(owner.Id, result.Id);
        }

        [Fact]
        public async Task ResolveOwnerAsync_TwoResolves_LastSeenMonotonic()
        {
            // Arrange
            var token = "monotonic";
            var hash = HashToken(token);
            var owner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-3)
            };
            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", token);

            // Act
            var first = await _service.ResolveOwnerAsync(ctx);
            var firstSeen = (await _dbContext.Owners.FindAsync(owner.Id))!.LastSeenAt;
            await Task.Delay(5); // ensure tick difference
            var second = await _service.ResolveOwnerAsync(ctx);
            var secondSeen = (await _dbContext.Owners.FindAsync(owner.Id))!.LastSeenAt;

            // Assert
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.True(secondSeen > firstSeen);
        }

        [Fact]
        public async Task ResolveOwnerAsync_Miss_DoesNotPersistChanges()
        {
            // Arrange
            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", "no-match");

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            Assert.Null(result);
            // No tracked changes written
            Assert.True(_dbContext.ChangeTracker.Entries().All(e => e.State == EntityState.Unchanged));
        }

        [Fact]
        public async Task ResolveOwnerAsync_RevokedInFuture_IsRejected()
        {
            // Rule assumed: any non-null RevokedAt means revoked, regardless of timestamp.
            var token = "future-revoked";
            var hash = HashToken(token);
            var owner = new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedAt = DateTimeOffset.UtcNow.AddDays(7) // in future
            };
            _dbContext.Owners.Add(owner);
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", token);

            var result = await _service.ResolveOwnerAsync(ctx);

            Assert.Null(result);
        }

        [Fact]
        public void SetAokCookie_IncludesPathAndCookieName()
        {
            var ctx = new DefaultHttpContext();
            _service.SetAokCookie(ctx.Response, "tok");

            Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
            var header = ctx.Response.Headers["Set-Cookie"].ToString().ToLowerInvariant();

            Assert.Contains("aok=", header);
            Assert.Contains("path=/", header); // ensure scoped to site root
                                               // Already checked elsewhere: httponly/secure/samesite
        }

        [Fact]
        public async Task ResolveOwnerAsync_TokenWithSpaces_IsNotTrimmed_Mismatch()
        {
            // Arrange: store owner with exact hash of trimmed token, but pass spaced token to resolve
            var raw = "edge-token";
            var spaced = "  edge-token  ";
            var hash = HashToken(raw);

            _dbContext.Owners.Add(new Owner
            {
                Id = Guid.NewGuid(),
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await _dbContext.SaveChangesAsync();

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = new MockCookieCollection();
            ((MockCookieCollection)ctx.Request.Cookies).Add("aok", spaced);

            // Act
            var result = await _service.ResolveOwnerAsync(ctx);

            // Assert
            // If your service TRIMS inputs, change this to Assert.NotNull(result).
            Assert.Null(result);
        }


        // Helper method to replicate the private HashToken method for testing
        private static string HashToken(string token)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(token);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToBase64String(hash)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'); // base64url
        }
    }

    // Mock cookie collection for testing
    public class MockCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _cookies = new();

        public string this[string key] => _cookies.TryGetValue(key, out var value) ? value : string.Empty;

        public int Count => _cookies.Count;

        public ICollection<string> Keys => _cookies.Keys;

        public bool ContainsKey(string key) => _cookies.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();

        public bool TryGetValue(string key, out string value) => _cookies.TryGetValue(key, out value!);

        IEnumerator IEnumerable.GetEnumerator() => _cookies.GetEnumerator();

        public void Add(string key, string value) => _cookies[key] = value;
    }
}
