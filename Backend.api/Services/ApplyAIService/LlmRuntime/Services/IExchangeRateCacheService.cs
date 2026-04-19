using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IExchangeRateCacheService
{
    Task<decimal> GetExchangeRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);

    Task<CurrencyExchangeRateQuote> GetExchangeRateQuoteAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);

    Task RefreshAsync(string baseCurrency, CancellationToken cancellationToken = default);
}