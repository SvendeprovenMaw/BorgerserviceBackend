#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using Openai.Library.Configuration;
using Openai.Library.Options;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Openai.Library.Phases
{
    public interface ICandidateEvidencePhase
    {
        Task<string> ExecutePhase(string jobRequirementsJson, BinaryData cv, List<BinaryData> s3Files);
    }

    public class CandidateEvidencePhase : ICandidateEvidencePhase
    {
        private readonly ChatClient _client;

        public CandidateEvidencePhase(IOptions<OpenAiLibraryOptions> options)
        {
            _client = new ChatClient(options.Value.Model, options.Value.SecretKey); // Eller ApiKey
        }

        public async Task<string> ExecutePhase(string jobRequirementsJson, BinaryData cv, List<BinaryData> s3Files)
        {
            string baseUserPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.BasePromptFileName);
            string systemPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.CandidateEvidencePromptFileName);
            string schemaJson = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.CandidateEvidenceSchemaFileName);

            var contentParts = new List<ChatMessageContentPart>();

//hvad gpt skal gøre
            contentParts.Add(ChatMessageContentPart.CreateTextPart(
                "Her er de udtrukne krav fra jobopslaget i JSON format:\n" +
                jobRequirementsJson +
                "\n\nAnalyser nu de vedhæftede dokumenter for at finde beviser for disse krav."));


            var uploadedFileNames = new List<string>();
            for (int i = 0; i < s3Files.Count; i++)
            {
                string fileName = $"candidate_doc_{i + 1}.pdf";
                uploadedFileNames.Add(fileName);

                contentParts.Add(ChatMessageContentPart.CreateFilePart(
                    s3Files[i],
                    "application/pdf",
                    fileName));
            }

            string fileInstruction = 
                "VIGTIGT: Følgende kandidat-dokumenter er vedhæftet til denne besked:\n" + 
                string.Join("\n", uploadedFileNames) + 
                "\n\nNår du opretter citations, SKAL du bruge præcis disse filnavne i dit 'document_id' felt.";

            contentParts.Add(ChatMessageContentPart.CreateTextPart(fileInstruction));

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(baseUserPrompt),
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(contentParts)
            };

            using JsonDocument doc = JsonDocument.Parse(schemaJson);
            JsonElement schemaElement = doc.RootElement.GetProperty("schema");
            string actualSchemaJson = schemaElement.GetRawText();

            ChatCompletionOptions options = new()
            {
                //Json schema til gpt
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "candidate_analysis",
                    BinaryData.FromString(actualSchemaJson),
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