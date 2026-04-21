using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Openai.Library.Configuration;
using Openai.Library.Options;
using OpenAI.Chat;

namespace Openai.Library.Phases
{
    public interface IApplicationGenerationPhase
    {
        Task<string> ExecutePhase(string requirementsJson, string evidenceJson, string matchingJson, string companyContextJson, string preferencesText);
    }

    public class ApplicationGenerationPhase : IApplicationGenerationPhase
    {
        //5
        private readonly ChatClient _client;

        public ApplicationGenerationPhase(IOptions<OpenAiLibraryOptions> options)
        {
            _client = new ChatClient("gpt-5.4-nano", options.Value.SecretKey);
        }

        /// <summary>
        /// Phase 5: Genererer det endelige output (f.eks. ansøgning) baseret på alle verificerede data.
        /// </summary>
        public async Task<string> ExecutePhase(
            string requirementsJson,
            string evidenceJson,
            string matchingJson,
            string companyContextJson,
            string preferencesText)
        {
            // Henter system prompt og schema specifikt til Phase 5
            string basePrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.BasePromptFileName);
            string systemPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.ApplicationGenrationPromptFileName);
            string schemaJson = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.ApplicationGenerationSchemaFileName);

            using JsonDocument doc = JsonDocument.Parse(schemaJson);
            JsonElement schemaElement = doc.RootElement.GetProperty("schema");
            string actualSchemaJson = schemaElement.GetRawText();

            var contentParts = new List<ChatMessageContentPart>();

            // Samler de 5 verificerede dokumenter til AI'en
            contentParts.Add(ChatMessageContentPart.CreateTextPart(
                "Her er de 5 kildedokumenter til din opgave:\n\n" +
                "--- 1. KRAV-DOKUMENT ---\n" + requirementsJson + "\n\n" +
                "--- 2. KANDIDAT-EVIDENS ---\n" + evidenceJson + "\n\n" +
                "--- 3. MATCHING-VURDERING ---\n" + matchingJson + "\n\n" +
                "--- 4. COMPANY CONTEXT ---\n" + companyContextJson + "\n\n" +
                "--- 5. SKRIVEPRÆFERENCER / PROFIL ---\n" + preferencesText
            ));

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(basePrompt),
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(contentParts)
            };

            // Tvinger igen AI'en til at overholde Strict JSON-format
            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "application_output",
                    BinaryData.FromString(actualSchemaJson),
                    jsonSchemaIsStrict: true
                ),
                // Vi giver den lidt mere kreativ frihed (Temperature = 0.5-0.7) her i Phase 5, 
                // hvis den skal skrive en overbevisende og naturlig tekst.
                Temperature = 0.6f
            };

            ChatCompletion completion = await _client.CompleteChatAsync(messages, options);

            return completion.Content[0].Text;
        }
    }
}