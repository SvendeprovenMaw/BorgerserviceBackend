using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Extensions;
using Backend.api.Services;
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
        private readonly IAiJobService _aiJobService;
        private readonly IUserService _userService;

        public AiController(IRequirementsPhase requirementsPhase,
        ICandidateEvidencePhase candidateEvidencePhase,
        ICompetenceMatchingPhase competenceMatchingPhase,
        IApplicationGenerationPhase applicationGenerationPhase,
        IAiJobService aiJobService,
        IUserService userService)
        {
            _requirementsPhase = requirementsPhase;
            this._evidencePhase = candidateEvidencePhase;
            this._competenceMatchingPhase = competenceMatchingPhase;
            this._applicationGenerationPhase = applicationGenerationPhase;
            this._aiJobService = aiJobService;
            this._userService = userService;
        }

        public class GenerationDto
        {
            public IFormFile JobPosting { get; set; }
            public IFormFile cv { get; set; }
            public IFormFile[] relavantDocuments { get; set; }
        }
        
        /// <summary>
        /// Goes through the whole process of analysing a job post, matching it with a cv and relevant documents and generating an application. Used for testing the whole flow in one go.
        /// </summary>
        /// <param name="fileUploadDto"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> RunThrough([FromForm] GenerationDto fileUploadDto)
        {
            var file = fileUploadDto.JobPosting;
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var requirementFile = await BinaryFileHelper.ToBinaryData(fileUploadDto.JobPosting);
            var requirements = await _requirementsPhase.AnalyseJobPost(requirementFile);

            var binaryDataList = await BinaryFileHelper.ToBinaryDataListAsync(fileUploadDto.relavantDocuments);
            var cvData = await BinaryFileHelper.ToBinaryData(fileUploadDto.cv);
            var evidence = await _evidencePhase.ExecutePhase(requirements, cvData, binaryDataList);

            var matches = await _competenceMatchingPhase.ExecutePhase(requirements, evidence);
            var application = await _applicationGenerationPhase.ExecutePhase(requirements, evidence, matches, "", "");
            return Ok(application);
        }

        /// <summary>
        /// Phase 2 endpoint for front end only analyses jobpost
        /// </summary>
        /// <param name="fileUploadDto"></param>
        /// <returns></returns>
        [HttpPost("Phase2")]
        public async Task<IActionResult> AnalyseJobPost([FromForm] AnalyseJobPostDto dto)
        {
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            
            if (!dto.jobPostPdf.Consent.ConsentGiven)
            {
                throw new ConsentNotGivenException();
            }
            var file = dto.jobPostPdf.File;
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var requirementFile = await BinaryFileHelper.ToBinaryData(file);
            var requirements = await _requirementsPhase.AnalyseJobPost(requirementFile);
            var aiJob = new AiProcessingJob(user, requirements);
            await _aiJobService.SaveAiJobAsync(aiJob);
            return Ok(aiJob);
        }

        [HttpPost("Phase3")]
        public async Task<IActionResult> AnalyseUserProfile([FromForm] AnalyseUserFilesDto dto)
        {
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            var relevantBinaryFiles = await BinaryFileHelper.ToBinaryDataListAsync(dto.OtherRelevantPdfs);
            
            var cvData = await BinaryFileHelper.ToBinaryData(dto.cv.File);
            var aiprossjob = await this._aiJobService.GetAiJobByIdAsync(dto.AiJob, user.Id);
            var evidence = await _evidencePhase.ExecutePhase(aiprossjob.JobRequirements, cvData, relevantBinaryFiles);
            aiprossjob.InsertUserCompetences(evidence);
            await _aiJobService.UpdateAiJobAsync(aiprossjob);
            return Ok(aiprossjob);
        }

        [HttpPost("Phase4")]
        public async Task<IActionResult> CompareRequirementsAndProfile([FromForm] CompareRequirementsAndProfileDto dto)
        {
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            var aiprossjob = await this._aiJobService.GetAiJobByIdAsync(dto.AiJob, user.Id);
            var matches = await _competenceMatchingPhase.ExecutePhase(aiprossjob.JobRequirements, aiprossjob.UserCompetences);
            aiprossjob.InsertMatches(matches);
            await _aiJobService.UpdateAiJobAsync(aiprossjob);
            return Ok(aiprossjob);
        }

        [HttpPost("Phase5")]
        public async Task<IActionResult> GenerateApplication([FromForm] GenerateApplicationDto dto)
        {
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            var aiprossjob = await this._aiJobService.GetAiJobByIdAsync(dto.AiJob, user.Id);
            var application = await _applicationGenerationPhase.ExecutePhase(aiprossjob.JobRequirements, aiprossjob.UserCompetences, aiprossjob.Matches, "", "");
            aiprossjob.InsertApplication(application);
            await _aiJobService.UpdateAiJobAsync(aiprossjob);
            return Ok(aiprossjob);
        }

        [HttpPost("CompleteApplication")]
        public async Task<IActionResult> CompleteApplication([FromForm] GenerateApplicationDto dto)
        {
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            var aiprossjob = await this._aiJobService.GetAiJobByIdAsync(dto.AiJob, user.Id);
            var application = await _applicationGenerationPhase.ExecutePhase(aiprossjob.JobRequirements, aiprossjob.UserCompetences, aiprossjob.Matches, "", "");
            aiprossjob.InsertApplication(application);
            await _aiJobService.UpdateAiJobAsync(aiprossjob);
            return Ok(application);
        }
    }
}