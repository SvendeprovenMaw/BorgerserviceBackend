using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class S3File
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public string S3Key { get; private set; } = "";
        public string ChecksumHash { get; private set; } = "";
        public DateTime UploadTime { get; set; }
    }
}