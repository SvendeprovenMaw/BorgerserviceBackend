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
    public interface ICompetenceMatchingPhase
    {
        Task<string> ExecutePhase(string jobRequirementsJson, string candidateEvidenceJson);
    }

    public class CompetenceMatchingPhase : ICompetenceMatchingPhase
    {
        //4
        private readonly ChatClient _client;

        public CompetenceMatchingPhase(IOptions<OpenAiLibraryOptions> options)
        {
            _client = new ChatClient(options.Value.Model, options.Value.SecretKey);
        }

        /// <summary>
        /// Udfører den endelige kompetencematchning baseret på krav og fundne beviser.
        /// </summary>
        /// <param name="jobRequirementsJson">Output fra Phase 2</param>
        /// <param name="candidateEvidenceJson">Output fra Phase 3</param>
        public async Task<string> ExecutePhase(string jobRequirementsJson, string candidateEvidenceJson)
        {
            // Vi bruger jeres statiske konfiguration til at hente prompts og schema
            string baseUserPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.BasePromptFileName);
            string systemPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.CompetencePromptFileName);
            string schemaJson = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.CompetenceMatchingSchemaFileName);

            using JsonDocument doc = JsonDocument.Parse(schemaJson);
            JsonElement schemaElement = doc.RootElement.GetProperty("schema");
            string actualSchemaJson = schemaElement.GetRawText();

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(baseUserPrompt),
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(
                    "Her er de udtrukne jobkrav:\n" + jobRequirementsJson + "\n\n" +
                    "Her er beviserne fundet i kandidatens dokumenter:\n" + candidateEvidenceJson + "\n\n" +
                    "Foretag nu en endelig kompetencevurdering og tildel match-scores.")
            };

            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "competence_matching_analysis",
                    BinaryData.FromString(actualSchemaJson),
                    jsonSchemaIsStrict: true
                )
            };

            ChatCompletion completion = await _client.CompleteChatAsync(messages, options);

            return completion.Content[0].Text;
        }
    }
}