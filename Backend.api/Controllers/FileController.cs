using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Backend.api.Services;
using JwtLibrary;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        private IConfiguration _conf;
        private IS3StorageService _s3;
        public FileController(IConfiguration conf, IS3StorageService s3)
        {
            this._conf = conf;
            this._s3 = s3;
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadDto file)
        {
            using (Stream stream = file.File.OpenReadStream())
            {
                await _s3.UploadFile(stream, file.File.Name, _conf["backblaze:keyname"]!, new User(JwtRoles.User, "", "", ""), FileCategory.Cv);
            }
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> DownloadFile(){ return NotFound(); }
        [HttpDelete]
        public async Task<IActionResult> DeleteFile(){ return NotFound(); }
    }
}