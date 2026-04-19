using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Enums;

namespace Backend.api.Entities.Dto
{
    /// <summary>
    /// Multipart request body for uploading one file into the backend storage layer.
    /// </summary>
    public class FileUploadDto
    {
        /// <summary>
        /// Human-readable name stored for the uploaded file.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Consent payload recorded together with the uploaded file.
        /// </summary>
        public GiveConsentDto Consent { get; set; } = null!;

        /// <summary>
        /// Binary file content to upload.
        /// </summary>
        public IFormFile File { get; set; } = null!;

        /// <summary>
        /// Storage category used to place the uploaded file under the user's career-document tree.
        /// </summary>
        public FileCategory Category { get; set; } = FileCategory.Cv;
        
    }
}