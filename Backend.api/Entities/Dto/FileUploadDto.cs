using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Enums;

namespace Backend.api.Entities.Dto
{
    public class FileUploadDto
    {
        public string Name { get; set; } = String.Empty;
        public GiveConsentDto Consent { get; set; }
        public IFormFile File { get; set; }
        public FileCategory FileCategory { get; set; }
        
    }
}