using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class CurrencyDisplayConversionService : ICurrencyDisplayConversionService
{
    private readonly IExchangeRateCacheService _exchangeRateCacheService;
    private readonly OpenAIOptions _openAiOptions;

    public CurrencyDisplayConversionService(
        IExchangeRateCacheService exchangeRateCacheService,
        IOptions<OpenAIOptions> openAiOptions)
    {
        _exchangeRateCacheService = exchangeRateCacheService;
        _openAiOptions = openAiOptions.Value;
    }

    public string DisplayCurrency => CurrencyCodeHelper.Normalize(_openAiOptions.DisplayCurrency);

    public async Task<CurrencyExchangeRateQuote> GetDisplayCurrencyQuoteAsync(string sourceCurrency, CancellationToken cancellationToken = default)
    {
        var normalizedSourceCurrency = CurrencyCodeHelper.NormalizeRequired(sourceCurrency, nameof(sourceCurrency));
        return await _exchangeRateCacheService.GetExchangeRateQuoteAsync(normalizedSourceCurrency, DisplayCurrency, cancellationToken);
    }

    public async Task<decimal> ConvertToDisplayCurrencyAsync(decimal amount, string sourceCurrency, CancellationToken cancellationToken = default)
    {
        var exchangeQuote = await GetDisplayCurrencyQuoteAsync(sourceCurrency, cancellationToken);
        if (!exchangeQuote.AppliedRate.HasValue)
        {
            throw new InvalidOperationException($"No exchange rate is available from {exchangeQuote.SourceCurrency} to {exchangeQuote.TargetCurrency}.");
        }

        return decimal.Round(amount * exchangeQuote.AppliedRate.Value, 8, MidpointRounding.AwayFromZero);
    }
}