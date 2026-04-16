using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Consent
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; } = null!;
        public bool ConsentGiven { get; private set; }
        protected Guid FileId { get; private set; }
        public S3File File { get; private set; } = null!;
        public Term TermsAccepted { get; private set; } = null!;
    }
}