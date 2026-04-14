using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class AiDraft
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        protected AiDraft() {}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public AiDraft(AiProcessingJob job, S3File draft)
        {
            this.Id = Guid.NewGuid();
            this.AiProcessingJob = job;
            this.Draft = draft;
        }
        public Guid Id { get; set; }
        public AiProcessingJob AiProcessingJob { get; set; }
        public S3File Draft { get; set; }
    }
}