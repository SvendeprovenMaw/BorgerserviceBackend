using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    public class FileUploadDto
    {
        public string Name { get; set; } = String.Empty;
        public bool ConsentGiven { get; set; } = false;
        public IFormFile File { get; set; }
        
    }
}