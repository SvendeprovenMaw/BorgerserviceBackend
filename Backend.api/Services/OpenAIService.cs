using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Backend.api.Services
{
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class OpenAIService
    {
        IConfiguration _conf;
        public OpenAIService(IConfiguration conf)
        {
            this._conf = conf;
        }

        public async Task<ResponseResult> ProcessFile(string fileurl)
        {
            ChatClient chatClient = new(model: "gpt-5.4-nano", "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");


            using HttpClient httpClient = new HttpClient();
            byte[] fileBytes = await httpClient.GetByteArrayAsync(fileurl);
            BinaryData fileData = BinaryData.FromBytes(fileBytes, "application/pdf");

            // 2. Upload to OpenAI
            //OpenAIFileClient fileClient = new("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");
            //OpenAIFile file = await fileClient.UploadFileAsync(fileData, "filename.pdf", FileUploadPurpose.UserData);

            ResponsesClient responsesClient = new(
                "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"
            );

            CreateResponseOptions options = new(
                "gpt-5.4-nano",
                new List<ResponseItem>()
                {
                    MessageResponseItem.CreateUserMessageItem(
                        new List<ResponseContentPart>()
                        {
                            ResponseContentPart.CreateInputTextPart("what is this"),
                            ResponseContentPart.CreateInputFilePart(new Uri(fileurl))
                        }
                    )
                }
            );
            return await responsesClient.CreateResponseAsync(options);

        }

        public async Task GenerateDraft()
        {
            
        }

        public async Task AnalyseJobPosting()
        {
            
        }

        public async Task AnalyseUserDocuments()
        {
            
        }


#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }
}