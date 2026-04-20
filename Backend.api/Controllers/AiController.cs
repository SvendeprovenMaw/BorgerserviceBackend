using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities.Dto;
using Microsoft.AspNetCore.Mvc;
using Openai.Library.Phases;

namespace Backend.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> AnalyseJobPost([FromForm] FileUploadDto fileUploadDto)
        {
            var file = fileUploadDto.File;
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileData = BinaryData.FromBytes(memoryStream.ToArray(), file.ContentType);

            ICompanyContextPhase companyContextPhase = new CompanyContextPhase();
            var result = await companyContextPhase.AnalyseJobPost(fileData);

            return Ok(result);
        }
    }
}