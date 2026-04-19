using Backend.api.Services.ApplyAIService;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.tests;

public sealed class ApplyAIServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplyAiServiceModule_RegistersApplyAiServiceAsBothInterfaces()
    {
        var services = new ServiceCollection();

        services.AddApplyAiServiceModule(CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-api-key",
        }));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IApplyAIService) && descriptor.ImplementationFactory != null);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IApplyAiJobExecutionService) && descriptor.ImplementationFactory != null);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ApplyAIService));
    }

    [Fact]
    public void AddApplyAiServiceModule_RegistersExecutionQueueAsBothIApplyAiExecutionQueueAndHostedService()
    {
        var services = new ServiceCollection();

        services.AddApplyAiServiceModule(CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-api-key",
        }));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ApplyAiExecutionQueue));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IApplyAiExecutionQueue) && descriptor.ImplementationFactory != null);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && descriptor.ImplementationFactory != null);
    }

    [Fact]
    public void AddApplyAiServiceModule_PostConfigurationFillsDefaultModelCurrenciesAndCatalogEntries()
    {
        using var provider = new ServiceCollection()
            .AddApplyAiServiceModule(CreateConfiguration(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-api-key",
            }))
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<OpenAIOptions>>().Value;

        options.Model.Should().Be(1);
        options.PricingCurrency.Should().Be("USD");
        options.DisplayCurrency.Should().Be("USD");
        options.Models.Should().ContainKey(1);
        options.Models.Should().ContainKey(2);
        options.Models.Should().ContainKey(3);
        options.Phases.CompanyContext.Model.Should().Be(1);
        options.Phases.ApplicationGeneration.Model.Should().Be(1);
    }

    [Fact]
    public void AddApplyAiServiceModule_ValidationRejectsMissingApiKeyWhenNoFallbackIsAvailable()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        try
        {
            using var provider = new ServiceCollection()
                .AddApplyAiServiceModule(CreateConfiguration(new Dictionary<string, string?>
                {
                    ["OpenAI:ApiKey"] = "",
                }))
                .BuildServiceProvider();

            var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

            act.Should().Throw<OptionsValidationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousApiKey);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void AddApplyAiServiceModule_ValidationRejectsInvalidCurrenciesAndBadModelSelections(Dictionary<string, string?> values)
    {
        using var provider = new ServiceCollection()
            .AddApplyAiServiceModule(CreateConfiguration(values))
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>();
    }

    public static TheoryData<Dictionary<string, string?>> InvalidConfigurations()
    {
        var data = new TheoryData<Dictionary<string, string?>>();

        data.Add(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-api-key",
            ["OpenAI:DisplayCurrency"] = "EURO",
        });

        data.Add(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-api-key",
            ["OpenAI:Model"] = "42",
        });

        return data;
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}