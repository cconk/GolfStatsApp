using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Golfer lookup by exact PGA Tour id or diacritics/case-insensitive name substring.</summary>
    public class GolferSearchService
    {
        private const string CacheKey = "prediction:golfer-directory";
        private readonly IPgaDataSource _pga;
        private readonly IFallbackPlayerSource _fallback;
        private readonly IMemoryCache _cache;
        private readonly ILogger<GolferSearchService> _logger;

        public GolferSearchService(IPgaDataSource pga, IFallbackPlayerSource fallback, IMemoryCache cache, ILogger<GolferSearchService> logger)
        {
            _pga = pga;
            _fallback = fallback;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<GolferSearchResult>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            query = query.Trim();
            var directory = await GetDirectoryAsync(ct);

            var exact = directory.Where(g => g.PlayerId == query).ToList();
            if (exact.Count > 0) return exact.Take(limit).ToList();

            var needle = Normalize(query);
            return directory
                .Where(g => Normalize(g.PlayerName).Contains(needle, StringComparison.Ordinal))
                .OrderBy(g => g.Rank ?? int.MaxValue).ThenBy(g => g.PlayerName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        /// <summary>Resolves a golfer by id (wins) or exact normalized name; null if unknown.</summary>
        public async Task<GolferSearchResult?> ResolveAsync(string? playerId, string? playerName, CancellationToken ct)
        {
            var directory = await GetDirectoryAsync(ct);
            if (!string.IsNullOrWhiteSpace(playerId))
                return directory.FirstOrDefault(g => g.PlayerId == playerId.Trim());
            if (string.IsNullOrWhiteSpace(playerName)) return null;
            var n = Normalize(playerName);
            var matches = directory.Where(g => Normalize(g.PlayerName) == n).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        public async Task<IReadOnlyList<GolferSearchResult>> GetDirectoryAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(CacheKey, out List<GolferSearchResult>? cached) && cached is { Count: > 0 })
                return cached;

            var byId = new Dictionary<string, GolferSearchResult>();
            try
            {
                foreach (var p in await _pga.GetPlayersAsync(ct))
                {
                    if (string.IsNullOrWhiteSpace(p.PlayerId) || string.IsNullOrWhiteSpace(p.DisplayName)) continue;
                    byId[p.PlayerId] = new GolferSearchResult { PlayerId = p.PlayerId, PlayerName = p.DisplayName.Trim() };
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "PGA player directory unavailable; using fallback player list");
            }

            try
            {
                foreach (var p in await _fallback.GetPlayersAsync())
                {
                    if (string.IsNullOrWhiteSpace(p.PlayerId)) continue;
                    if (byId.TryGetValue(p.PlayerId, out var existing))
                        existing.Rank = p.Rank > 0 ? p.Rank : existing.Rank;
                    else
                        byId[p.PlayerId] = new GolferSearchResult { PlayerId = p.PlayerId, PlayerName = p.PlayerName, Rank = p.Rank > 0 ? p.Rank : null };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback player list unavailable");
            }

            var list = byId.Values.ToList();
            if (list.Count > 0) _cache.Set(CacheKey, list, TimeSpan.FromHours(6));
            return list;
        }

        public static string Normalize(string value)
        {
            var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c switch { 'ø' or 'Ø' => 'o', 'æ' or 'Æ' => 'a', 'ß' => 's', 'đ' or 'Đ' => 'd', 'ł' or 'Ł' => 'l', _ => char.ToLowerInvariant(c) });
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
