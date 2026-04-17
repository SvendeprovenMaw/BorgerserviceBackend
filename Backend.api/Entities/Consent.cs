using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Consent
    {
        private Consent(){}
        public Consent(User user, S3File file, DateTime timeOfConsent)
        {
            this.Id = Guid.NewGuid();
            this.User = user;
            this.File = file;
            this.FileId = file.Id;
            this.TimeOfConsent = timeOfConsent;
        }
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; } = null!;
        public bool ConsentRetracted { get; private set; }
        public DateTime TimeOfConsent { get; set; }
        public Guid FileId { get; private set; }
        public S3File File { get; private set; } = null!;
    }
}