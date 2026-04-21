using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities.Dto;
using Backend.api.Extensions;
using Microsoft.AspNetCore.Mvc;
using Openai.Library.Phases;

namespace Backend.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiController : ControllerBase
    {
        private readonly IRequirementsPhase _requirementsPhase;
        private readonly ICandidateEvidencePhase _evidencePhase;
        private readonly ICompetenceMatchingPhase _competenceMatchingPhase;
        private readonly IApplicationGenerationPhase _applicationGenerationPhase;

        public AiController(IRequirementsPhase requirementsPhase, ICandidateEvidencePhase candidateEvidencePhase, ICompetenceMatchingPhase competenceMatchingPhase, IApplicationGenerationPhase applicationGenerationPhase)
        {
            _requirementsPhase = requirementsPhase;
            this._evidencePhase = candidateEvidencePhase;
            this._competenceMatchingPhase = competenceMatchingPhase;
            this._applicationGenerationPhase = applicationGenerationPhase;
        }

        public class GenerationDto
        {
            public IFormFile JobPosting { get; set; }
            public IFormFile cv { get; set; }
            public IFormFile[] relavantDocuments { get; set; }
        }
        
        
        [HttpPost]
        public async Task<IActionResult> AnalyseJobPost([FromForm] GenerationDto fileUploadDto)
        {
            var file = fileUploadDto.JobPosting;
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileData = BinaryData.FromBytes(memoryStream.ToArray(), file.ContentType);

            var requirements = await _requirementsPhase.AnalyseJobPost(fileData);

            var binaryDataList = await BinaryFileHelper.ToBinaryDataListAsync(fileUploadDto.relavantDocuments);

            await fileUploadDto.cv.CopyToAsync(memoryStream);
            var cvData = BinaryData.FromBytes(memoryStream.ToArray(), file.ContentType);

            var evidence = await _evidencePhase.ExecutePhase(requirements, cvData, binaryDataList);

            var matches = await _competenceMatchingPhase.ExecutePhase(requirements, evidence);

            var application = await _applicationGenerationPhase.ExecutePhase(requirements, evidence, matches, "", "");
            return Ok(new {requirements, evidence, matches, application});
        }
    }
}