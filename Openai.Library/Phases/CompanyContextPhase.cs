#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Openai.Library.Phases
{
    public interface ICompanyContextPhase
    {
        Task<string> AnalyseJobPost(BinaryData fileData);
    }

    public class CompanyContextPhase : ICompanyContextPhase
    {
        //1

        public async Task<string> AnalyseJobPost(BinaryData fileData)
        {
            ChatClient chatClient = new(model: "gpt-5.4-nano", "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");

            string systemPrompt = await File.ReadAllTextAsync("F:/GitHub/BorgerserviceBackend/Openai.Library/Assets/Promts/company_context.prompt");
            string baseUserPrompt = await File.ReadAllTextAsync("F:/GitHub/BorgerserviceBackend/Openai.Library/Assets/Promts/base.prompt");
            string jsonSchemaContent = await File.ReadAllTextAsync("F:/GitHub/BorgerserviceBackend/Openai.Library/Assets/Schema/LLMParsing/company_context_schema.json");

            using JsonDocument doc = JsonDocument.Parse(jsonSchemaContent);
            JsonElement schemaElement = doc.RootElement.GetProperty("schema");
            string actualSchemaJson = schemaElement.GetRawText();

            ResponsesClient responsesClient = new(
                "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"
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
