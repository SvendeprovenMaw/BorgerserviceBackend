using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Profile
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private Profile(){}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Profile(User user)
        {
            this.Id = Guid.NewGuid();
            this.User = user;
            this.UserId = user.Id;
        }
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; } = null!;
        public S3File? CurrentCv { get; private set; }
        public Collection<S3File>? RelevantDocuments { get; private set; }
    }
}