using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Backend.api.Services;
using JwtLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        private IConfiguration _conf;
        private IS3StorageService _s3;
        private readonly IUserService _userService;
        public FileController(IConfiguration conf, IUserService user, IS3StorageService s3)
        {
            this._conf = conf;
            this._s3 = s3;
            this._userService = user;
        }

        [HttpPost]
        [Authorize]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadDto file)
        {
            var user = await _userService.GetUser(HttpContext.User);
            using (Stream stream = file.File.OpenReadStream())
            {
                await _s3.UploadFile(stream, file.Consent, file.Name, user, FileCategory.Cv);
            }
            return Ok();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> DownloadFile(Guid fileId){ 
            var user = await _userService.GetUser(HttpContext.User);
            Console.WriteLine(fileId);
            var presignedUrl = await _s3.LinkToFIle(fileId, user);
            return Ok(presignedUrl); 
        }
        
        [HttpGet("FileStructure")]
        [Authorize]
        public async Task<IActionResult> GetFileStructure(){ 
            var user = await _userService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            var fileStructure = await _s3.GetFileStructure(user.Id);
            return Ok(fileStructure); 
        }
        [HttpDelete]
        public async Task<IActionResult> DeleteFile(){ return NotFound(); }
    }
}