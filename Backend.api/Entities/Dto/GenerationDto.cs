namespace Backend.api.Entities.Dto
{
    public class GenerationDto
    {
        public IFormFile JobPosting { get; set; }
        public IFormFile cv { get; set; }
        public IFormFile[] relavantDocuments { get; set; }
    }
}