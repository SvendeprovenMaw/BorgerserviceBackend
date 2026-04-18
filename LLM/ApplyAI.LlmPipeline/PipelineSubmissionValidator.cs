namespace ApplyAI.LlmPipeline;

public static class PipelineSubmissionValidator
{
    public static IReadOnlyList<PipelineValidationIssue> Validate(PipelineSubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<PipelineValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.ApplicantId))
        {
            issues.Add(new PipelineValidationIssue("applicant_id_missing", "ApplicantId is required."));
        }

        if (request.JobListingSource is null)
        {
            issues.Add(new PipelineValidationIssue("job_listing_source_missing", "A job listing source is required."));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.JobListingSource.Reference))
        {
            issues.Add(new PipelineValidationIssue("job_listing_reference_missing", "Job listing source reference is required."));
        }

        switch (request.JobListingSource.Kind)
        {
            case PipelineInputKind.UploadedFile when string.IsNullOrWhiteSpace(request.JobListingSource.FileName):
                issues.Add(new PipelineValidationIssue("job_listing_filename_missing", "UploadedFile sources must include FileName."));
                break;

            case PipelineInputKind.RemoteUrl:
                if (!Uri.TryCreate(request.JobListingSource.Reference, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    issues.Add(new PipelineValidationIssue("job_listing_url_invalid", "RemoteUrl sources must be absolute http or https URLs."));
                }
                break;
        }

        return issues;
    }

    public static void EnsureValid(PipelineSubmissionRequest request)
    {
        var issues = Validate(request);
        if (issues.Count == 0)
        {
            return;
        }

        throw new ArgumentException(string.Join(" ", issues.Select(issue => issue.Message)), nameof(request));
    }
}