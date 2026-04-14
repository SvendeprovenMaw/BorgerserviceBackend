using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Profile
    {
        public Guid Id { get; private set; }
        public User User { get; private set; }
        public S3File? CurrentCv { get; private set; }
        public Collection<S3File>? RelevantDocuments { get; private set; }
    }
}