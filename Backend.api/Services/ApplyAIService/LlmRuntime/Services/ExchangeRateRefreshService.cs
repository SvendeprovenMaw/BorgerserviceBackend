using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class ExchangeRateRefreshService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    private readonly IExchangeRateCacheService _exchangeRateCacheService;
    private readonly OpenAIOptions _openAiOptions;
    private readonly ILogger<ExchangeRateRefreshService> _logger;

    public ExchangeRateRefreshService(
        IExchangeRateCacheService exchangeRateCacheService,
        IOptions<OpenAIOptions> openAiOptions,
        ILogger<ExchangeRateRefreshService> logger)
    {
        _exchangeRateCacheService = exchangeRateCacheService;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pricingCurrency = CurrencyCodeHelper.Normalize(_openAiOptions.PricingCurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _exchangeRateCacheService.RefreshAsync(pricingCurrency, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Exchange-rate refresh failed for pricing currency {PricingCurrency}. Existing cached rates, if any, will continue to be used.",
                    pricingCurrency);
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}