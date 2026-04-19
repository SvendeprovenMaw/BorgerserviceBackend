using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class ExchangeRateCacheService : IExchangeRateCacheService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExchangeRateCacheService> _logger;
    private readonly ConcurrentDictionary<string, CachedExchangeRates> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.OrdinalIgnoreCase);

    public ExchangeRateCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<ExchangeRateCacheService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<decimal> GetExchangeRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default)
    {
        var exchangeRateQuote = await GetExchangeRateQuoteAsync(baseCurrency, targetCurrency, cancellationToken);
        if (!exchangeRateQuote.AppliedRate.HasValue)
        {
            throw new InvalidOperationException($"Exchange-rate data is unavailable from {exchangeRateQuote.SourceCurrency} to {exchangeRateQuote.TargetCurrency}.");
        }

        return exchangeRateQuote.AppliedRate.Value;
    }

    public async Task<CurrencyExchangeRateQuote> GetExchangeRateQuoteAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default)
    {
        var normalizedBaseCurrency = CurrencyCodeHelper.NormalizeRequired(baseCurrency, nameof(baseCurrency));
        var normalizedTargetCurrency = CurrencyCodeHelper.NormalizeRequired(targetCurrency, nameof(targetCurrency));

        if (string.Equals(normalizedBaseCurrency, normalizedTargetCurrency, StringComparison.Ordinal))
        {
            return new CurrencyExchangeRateQuote
            {
                ApiAvailability = CurrencyExchangeRateQuote.NotRequiredAvailability,
                SourceCurrency = normalizedBaseCurrency,
                TargetCurrency = normalizedTargetCurrency,
                AppliedRate = 1m,
                UsingStaleRate = false
            };
        }

        try
        {
            var cacheEntry = await GetOrRefreshCacheEntryAsync(normalizedBaseCurrency, forceRefresh: false, cancellationToken);
            return BuildExchangeRateQuote(cacheEntry, normalizedBaseCurrency, normalizedTargetCurrency, CurrencyExchangeRateQuote.OnlineAvailability, usingStaleRate: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Exchange-rate lookup failed for base currency {BaseCurrency}. Latest cached data will be used if available.",
                normalizedBaseCurrency);

            if (_cache.TryGetValue(normalizedBaseCurrency, out var cached))
            {
                return BuildExchangeRateQuote(cached, normalizedBaseCurrency, normalizedTargetCurrency, CurrencyExchangeRateQuote.OfflineAvailability, usingStaleRate: true);
            }

            return new CurrencyExchangeRateQuote
            {
                ApiAvailability = CurrencyExchangeRateQuote.OfflineAvailability,
                SourceCurrency = normalizedBaseCurrency,
                TargetCurrency = normalizedTargetCurrency,
                AppliedRate = null,
                UsingStaleRate = false
            };
        }
    }

    public async Task RefreshAsync(string baseCurrency, CancellationToken cancellationToken = default)
    {
        var normalizedBaseCurrency = CurrencyCodeHelper.NormalizeRequired(baseCurrency, nameof(baseCurrency));
        _ = await GetOrRefreshCacheEntryAsync(normalizedBaseCurrency, forceRefresh: true, cancellationToken);
    }

    private async Task<CachedExchangeRates> GetOrRefreshCacheEntryAsync(string baseCurrency, bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh
            && _cache.TryGetValue(baseCurrency, out var cached)
            && cached.ExpiresAtUtc > now)
        {
            return cached;
        }

        var refreshLock = _refreshLocks.GetOrAdd(baseCurrency, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!forceRefresh
                && _cache.TryGetValue(baseCurrency, out cached)
                && cached.ExpiresAtUtc > now)
            {
                return cached;
            }

            var snapshot = await FetchSnapshotAsync(baseCurrency, cancellationToken);
            var refreshedAtUtc = DateTimeOffset.UtcNow;
            cached = new CachedExchangeRates(snapshot, refreshedAtUtc.Add(CacheLifetime), refreshedAtUtc);
            _cache[baseCurrency] = cached;
            return cached;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static CurrencyExchangeRateQuote BuildExchangeRateQuote(
        CachedExchangeRates cached,
        string baseCurrency,
        string targetCurrency,
        string apiAvailability,
        bool usingStaleRate)
    {
        cached.Snapshot.Rates.TryGetValue(targetCurrency, out var exchangeRate);

        return new CurrencyExchangeRateQuote
        {
            ApiAvailability = apiAvailability,
            SourceCurrency = cached.Snapshot.BaseCurrency,
            TargetCurrency = targetCurrency,
            AppliedRate = exchangeRate > 0m ? exchangeRate : null,
            UsingStaleRate = usingStaleRate,
            ProviderLastUpdateUtc = cached.Snapshot.ProviderLastUpdateUtc,
            ProviderNextUpdateUtc = cached.Snapshot.ProviderNextUpdateUtc,
            LastSuccessfulRefreshUtc = cached.LastSuccessfulRefreshUtc
        };
    }

    private async Task<ExchangeRateSnapshot> FetchSnapshotAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("exchange-rate-api");
        using var response = await client.GetAsync($"v6/latest/{baseCurrency}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ExchangeRateApiResponse>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("Exchange-rate API returned an empty response.");
        }

        if (!string.Equals(payload.Result, "success", StringComparison.OrdinalIgnoreCase))
        {
            var errorType = string.IsNullOrWhiteSpace(payload.ErrorType) ? "unknown_error" : payload.ErrorType;
            throw new InvalidOperationException($"Exchange-rate API request failed with result '{payload.Result ?? "unknown"}' and error '{errorType}'.");
        }

        if (payload.Rates is null || payload.Rates.Count == 0)
        {
            throw new InvalidOperationException("Exchange-rate API returned no conversion rates.");
        }

        _logger.LogInformation(
            "Exchange rates refreshed for base currency {BaseCurrency}. Provider next update: {NextUpdateUtc}.",
            baseCurrency,
            payload.TimeNextUpdateUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(payload.TimeNextUpdateUnix.Value) : null);

        return new ExchangeRateSnapshot(
            BaseCurrency: CurrencyCodeHelper.Normalize(payload.BaseCode, baseCurrency),
            ProviderLastUpdateUtc: payload.TimeLastUpdateUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(payload.TimeLastUpdateUnix.Value) : null,
            ProviderNextUpdateUtc: payload.TimeNextUpdateUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(payload.TimeNextUpdateUnix.Value) : null,
            Rates: payload.Rates);
    }

    private sealed record CachedExchangeRates(ExchangeRateSnapshot Snapshot, DateTimeOffset ExpiresAtUtc, DateTimeOffset LastSuccessfulRefreshUtc);

    private sealed record ExchangeRateSnapshot(
        string BaseCurrency,
        DateTimeOffset? ProviderLastUpdateUtc,
        DateTimeOffset? ProviderNextUpdateUtc,
        IReadOnlyDictionary<string, decimal> Rates);

    private sealed class ExchangeRateApiResponse
    {
        [JsonPropertyName("result")]
        public string? Result { get; init; }

        [JsonPropertyName("error-type")]
        public string? ErrorType { get; init; }

        [JsonPropertyName("base_code")]
        public string? BaseCode { get; init; }

        [JsonPropertyName("time_last_update_unix")]
        public long? TimeLastUpdateUnix { get; init; }

        [JsonPropertyName("time_next_update_unix")]
        public long? TimeNextUpdateUnix { get; init; }

        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; init; }
    }
}