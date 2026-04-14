namespace OpenAiResponses.Api.Services;

/// <summary>
/// Converts monetary values into the configured reporting currency.
/// </summary>
public interface ICurrencyDisplayConversionService
{
    string DisplayCurrency { get; }

    Task<CurrencyExchangeRateQuote> GetDisplayCurrencyQuoteAsync(string sourceCurrency, CancellationToken cancellationToken = default);

    Task<decimal> ConvertToDisplayCurrencyAsync(decimal amount, string sourceCurrency, CancellationToken cancellationToken = default);
}