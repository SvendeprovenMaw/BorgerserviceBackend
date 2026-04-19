using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface ICurrencyDisplayConversionService
{
    string DisplayCurrency { get; }

    Task<CurrencyExchangeRateQuote> GetDisplayCurrencyQuoteAsync(string sourceCurrency, CancellationToken cancellationToken = default);

    Task<decimal> ConvertToDisplayCurrencyAsync(decimal amount, string sourceCurrency, CancellationToken cancellationToken = default);
}