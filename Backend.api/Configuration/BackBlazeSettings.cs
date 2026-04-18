namespace Backend.api.Configuration
{
    public class BackBlazeSettings
    {
        public string KeyName { get; set; } = string.Empty;
        public string Keyid { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty;
        public string ApplicationKey { get; set; } = string.Empty;
        public string ServiceUrl { get; set; } = string.Empty;
        public string DownloaderAuthenticationRegion { get; set; } = string.Empty;
        public string DownloaderRegionSystemName { get; set; } = string.Empty;
        public bool ForcePathStyle { get; set; } = true;
        public string UploaderAuthenticationRegion { get; set; } = string.Empty;
        public bool UploaderUseHttp { get; set; }
    }
}