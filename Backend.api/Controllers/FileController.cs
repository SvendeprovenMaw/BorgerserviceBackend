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
    /// <summary>
    /// File-storage routes for uploading user documents, retrieving download links, and browsing the stored file structure.
    /// </summary>
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

        /// <summary>
        /// Uploads one user document to the backend storage layer.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **name**: the display name that should be stored for the uploaded file.
        /// - **consent**: the consent payload recorded for this upload, including whether consent was granted and when it was granted.
        /// - **file**: the binary file stream to upload.
        ///
        /// The current implementation stores uploaded files through the S3 storage service and classifies them as CV files.
        /// </remarks>
        /// <param name="file">Multipart form payload containing the uploaded file, display name, and consent metadata.</param>
        /// <returns>An OK result when the file upload succeeds.</returns>
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

        /// <summary>
        /// Returns a short-lived download URL for one stored file.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **fileId**: the id of the stored file the current user wants to download.
        ///
        /// The route resolves ownership through the authenticated user and returns a pre-signed storage URL when access is allowed.
        /// </remarks>
        /// <param name="fileId">Stored file id to download.</param>
        /// <returns>A pre-signed download URL for the requested file.</returns>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> DownloadFile([FromQuery] Guid fileId){ 
            var user = await _userService.GetUser(HttpContext.User);
            var presignedUrl = await _s3.UserDownloadFile(fileId, user);
            return Ok(presignedUrl); 
        }
        
        /// <summary>
        /// Returns the authenticated user's stored file structure.
        /// </summary>
        /// <remarks>
        /// This route does not require a request body.
        ///
        /// It resolves the current user from the auth cookie and returns the file/folder structure exposed by the storage service for that user.
        /// </remarks>
        /// <returns>The current user's stored file structure.</returns>
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

        /// <summary>
        /// Reserved delete route for future file-removal support.
        /// </summary>
        /// <remarks>
        /// The route is present in the controller but is not implemented yet and currently always returns `404 Not Found`.
        /// </remarks>
        /// <returns>A not-found response because deletion is not implemented yet.</returns>
        [HttpDelete]
        public async Task<IActionResult> DeleteFile(){ return NotFound(); }
    }
}