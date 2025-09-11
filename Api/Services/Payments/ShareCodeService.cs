using Api.Data;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Api.Services.Payments
{
    public sealed class ShareCodeService(LightningDbContext db) : IShareCodeService
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/I/1
        public async Task<string> GenerateUniqueAsync(int len = 8, CancellationToken ct = default)
        {
            while (true)
            {
                var code = NewCode(len);
                var exists = await db.SplitSessions.AnyAsync(s => s.ShareCode == code, ct);
                if (!exists) return code;
            }
        }
        private static string NewCode(int len)
        {
            Span<byte> buf = stackalloc byte[len];
            RandomNumberGenerator.Fill(buf);
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = Alphabet[buf[i] % Alphabet.Length];
            return new string(chars);
        }
    }
}
