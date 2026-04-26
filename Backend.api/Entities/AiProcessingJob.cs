using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class AiProcessingJob
    {
        private AiProcessingJob()
        {
            
        }
        public AiProcessingJob(User user)
        {
            Id = Guid.NewGuid();
            User = user;
        }

        public AiProcessingJob(User user, string jobRequirements) : this(user)
        {
            this.JobRequirements = jobRequirements;
        }

        public void InsertUserCompetences(string userCompetences)
        {
            this.UserCompetences = userCompetences;
        }

        public void InsertMatches(string matches)
        {
            this.Matches = matches;
        }

        public void InsertApplication(string application)
        {
            this.Application = application;
        }

        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; }
        public S3File? ResultFile { get; private set; }
        public Collection<S3File>? ProcessedFiles { get; private set; }
        public string JobRequirements { get; private set; } = string.Empty;
        public string UserCompetences { get; private set; } = string.Empty;
        public string Matches { get; private set; } = string.Empty;
        public string Application { get; private set; } = string.Empty;
    }
}