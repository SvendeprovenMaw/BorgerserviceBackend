using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class AiProcessingJob
    {
        public Guid Id { get; private set; }
        public S3File? ResultFile { get; private set; }
        public Collection<S3File>? ProcessedFiles { get; private set; }
    }
}