using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    public class AnalyseJobPostDto
    {
        public FileUploadDto cv { get; set; }
        public FileUploadDto[] OtherRelevantPdfs { get; set; }
    }
}