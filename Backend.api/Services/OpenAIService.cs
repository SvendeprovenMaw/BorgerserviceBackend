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
            ChatClient chatClient = new(model: "gpt-5.4-nano", _conf["OpenAi:SecretKey"]);


            using HttpClient httpClient = new HttpClient();
            byte[] fileBytes = await httpClient.GetByteArrayAsync(fileurl);
            BinaryData fileData = BinaryData.FromBytes(fileBytes, "application/pdf");

            ResponsesClient responsesClient = new(
                _conf["OpenAi:SecretKey"]
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