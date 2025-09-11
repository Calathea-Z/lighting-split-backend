using Api.Data;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Api.Services.Payments
{
    public sealed class ShareCodeService(LightningDbContext db) : IShareCodeService
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/I/1
        private const int MaxAttempts = 32; // safety cap for pathological collisions

        public async Task<string> GenerateUniqueAsync(int len = 8, CancellationToken ct = default)
        {
            if (len <= 0) throw new ArgumentOutOfRangeException(nameof(len), "length must be >= 1");
            ct.ThrowIfCancellationRequested();

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = NewCode(len);
                if (!await db.SplitSessions.AnyAsync(s => s.ShareCode == code, ct))
                    return code;

                ct.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException("Failed to generate a unique share code after max retries.");
        }

        private static string NewCode(int len)
        {
            Span<byte> buf = stackalloc byte[len];
            RandomNumberGenerator.Fill(buf);

            var chars = new char[len];
            for (int i = 0; i < len; i++)
                chars[i] = Alphabet[buf[i] % Alphabet.Length];

            return new string(chars);
        }
    }
}
