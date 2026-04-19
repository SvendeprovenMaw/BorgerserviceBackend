namespace OpenAiResponses.Api.Options;

/// <summary>
/// Configuration for locating sample assets and default runtime behavior in the test pipeline.
/// </summary>
public sealed class SamplePipelineOptions
{
    public const string SectionName = "SamplePipeline";

    public string CandidateRootPath { get; set; } = Path.Combine("TestData", "Borgere");

    public string DefaultCandidateDirectory { get; set; } = "Borger1";

    public string PreferencesFileName { get; set; } = "Preferences.json";

    public string JobListingsPath { get; set; } = Path.Combine("TestData", "Opslag");

    public string ParsingSchemasPath { get; set; } = Path.Combine("Backend.api", "Services", "ApplyAIService", "Assets", "Schemas", "LLM Parsing");

    public string ResultsPath { get; set; } = Path.Combine("Backend.api", "Services", "ApplyAIService", "Assets", "MockResults");

    public string RunDirectoryPrefix { get; set; } = "Run ";

    public SampleFitStrategyDefaultsOptions DefaultFitStrategy { get; set; } = new();

    public CoverLetterTemplateOptions CoverLetterTemplate { get; set; } = new();
}

/// <summary>
/// Fallback fit-strategy values used when no candidate preferences file is present.
/// </summary>
public sealed class SampleFitStrategyDefaultsOptions
{
    public string GuidanceMode { get; set; } = "optimistic";

    public bool IncludeFitAdvisory { get; set; }

    public bool AllowApplicationOnWeakMatch { get; set; } = true;

    public bool PreferTransferableStrengthsWhenDirectMatchIsWeak { get; set; } = true;

    public bool AllowStretchPositioning { get; set; } = true;
}

/// <summary>
/// Paths, filenames, and layout constraints for the rendered HTML cover letter.
/// </summary>
public sealed class CoverLetterTemplateOptions
{
    public string TemplatesPath { get; set; } = Path.Combine("Backend.api", "Services", "ApplyAIService", "Assets", "Templates");

    public string HtmlTemplateFileName { get; set; } = "cover-letter-template.html";

    public string CssTemplateFileName { get; set; } = "cover-letter-template.css";

    public string OutputDirectoryName { get; set; } = "cover_letter";

    public string RenderedHtmlFileName { get; set; } = "cover_letter.html";

    public string RenderedCssFileName { get; set; } = "cover_letter.css";

    public string RenderedPdfFileName { get; set; } = "cover_letter.pdf";

    public string RenderSummaryFileName { get; set; } = "cover_letter_render_summary.json";

    public int EstimatedCharactersPerLine { get; set; } = 72;

    public int MaxMainContentCharacters { get; set; } = 1550;
}