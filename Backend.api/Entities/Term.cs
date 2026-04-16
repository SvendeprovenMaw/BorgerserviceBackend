using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Term : S3File
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private Term() : base() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Term(User user, string filename, string s3Key, string checksumHash, string version, bool active = false) : base(user, filename, s3Key, checksumHash)
        {
            this.Version = version;
            this.Active = active;
        }

        public string Version { get; set; }
        public bool Active { get; set; }
    }
}