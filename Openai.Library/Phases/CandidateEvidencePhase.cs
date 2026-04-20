#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Openai.Library.Configuration;
using Openai.Library.Options;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Openai.Library.Phases
{
    public class CandidateEvidencePhase
    {
        private readonly ChatClient _client;

        // Væk med resourceConfig fra constructoren!
        public CandidateEvidencePhase(IOptions<OpenAiLibraryOptions> options)
        {
            _client = new ChatClient("gpt-4o", options.Value.SecretKey); // Eller ApiKey
        }

        public async Task<string> ExecutePhase(string jobRequirementsJson, List<BinaryData> s3Files)
        {
            // Vi kalder den statiske klasse direkte!
            string systemPrompt = AiResourceConfiguration.GetResourceContent("candidate_evidence.prompt");
            string schemaJson = AiResourceConfiguration.GetResourceContent("candidate_evidence_schema.json");

            var contentParts = new List<ChatMessageContentPart>();

            // 1. Instruktion og jobkrav
            contentParts.Add(ChatMessageContentPart.CreateTextPart(
                "Her er de udtrukne krav fra jobopslaget i JSON format:\n" +
                jobRequirementsJson + 
                "\n\nAnalyser nu de vedhæftede dokumenter for at finde beviser for disse krav."));

            // 2. Fodr PDF'erne direkte til gpt-4o som BinaryData
            for (int i = 0; i < s3Files.Count; i++)
            {
                // CreateFilePart sender bytes direkte til OpenAI
                contentParts.Add(ChatMessageContentPart.CreateFilePart(
                    s3Files[i], 
                    "application/pdf", 
                    $"document_{i+1}.pdf"));
            }

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(contentParts)
            };

            // 3. Konfigurer JSON-schemaet
            ChatCompletionOptions options = new()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "candidate_analysis",
                    BinaryData.FromString(schemaJson),
                    jsonSchemaIsStrict: true
                )
            };

            // 4. Udfør AI-kaldet
            ChatCompletion completion = await _client.CompleteChatAsync(messages, options);
            
            return completion.Content[0].Text;
        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.