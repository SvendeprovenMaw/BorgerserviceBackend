#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openai.Library.Configuration;
using Openai.Library.Options;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Openai.Library.Phases
{
    public interface IRequirementsPhase
    {
        Task<string> AnalyseJobPost(BinaryData fileData);
    }

    public class RequirementsPhase : IRequirementsPhase
    {
        private readonly OpenAiLibraryOptions _options;

        public RequirementsPhase(IOptions<OpenAiLibraryOptions> options)
        {
            _options = options.Value;
        }
        //2
        public async Task<string> AnalyseJobPost(BinaryData fileData)
        {
            ChatClient chatClient = new(model: "gpt-5.4-nano", this._options.SecretKey);

            string systemPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.RequirementsFileName);
            string baseUserPrompt = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.BasePromptFileName);
            string jsonSchemaContent = AiResourceConfiguration.GetResourceContent(AiResourceConfiguration.RequirementsSchemaFileName);

            using JsonDocument doc = JsonDocument.Parse(jsonSchemaContent);
            JsonElement schemaElement = doc.RootElement.GetProperty("schema");
            string actualSchemaJson = schemaElement.GetRawText();

            ResponsesClient responsesClient = new(
                _options.SecretKey
            );

            // 4. Konfigurer Structured Output formatet
            ChatResponseFormat jsonFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "job_analysis",
                jsonSchema: BinaryData.FromString(actualSchemaJson),
                jsonSchemaIsStrict: true
            );

            // 2. Opret dine beskeder
            List<ChatMessage> messages = new()
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(
                    ChatMessageContentPart.CreateTextPart(baseUserPrompt),
                    ChatMessageContentPart.CreateFilePart(fileData, "application/pdf", "job_posting.pdf")
                )
            };

            // 3. Tilføj formatet til ChatCompletionOptions
            ChatCompletionOptions options = new()
            {
                ResponseFormat = jsonFormat // I ChatClient hedder den ResponseFormat!
            };
            ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);
            return completion.Content[0].Text;

        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.