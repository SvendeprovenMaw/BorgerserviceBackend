namespace OpenAiResponses.Api.Services;

/// <summary>
/// Provides cached exchange-rate lookups with periodic refresh support.
/// </summary>
public interface IExchangeRateCacheService
{
    Task<decimal> GetExchangeRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);

    Task<CurrencyExchangeRateQuote> GetExchangeRateQuoteAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);

    Task RefreshAsync(string baseCurrency, CancellationToken cancellationToken = default);
}