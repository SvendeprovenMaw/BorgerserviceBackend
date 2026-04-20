using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Openai.Library.Configuration
{
    public static class AiResourceConfiguration
    {
        // Navnene på de indlejrede filer (uden stier, da vi leder efter dem i DLL'en)
        public static string CompanyContextFileName { get; } = "company_context.prompt";
        public static string RequirementsFileName { get; } = "requirements.prompt";
        public static string CandidateEvidenceFileName { get; } = "candidate_evidence.prompt";
        public static string BasePromptFileName { get; } = "base.prompt";
        public static string RequirementsSchemaFileName { get; } = "requirements_schema.json";
        public static string CandidateEvidenceSchemaFileName { get; } = "candidate_evidence_schema.json";

        // En hjælper til at læse indholdet direkte fra denne konfiguration
        public static string GetResourceContent(string fileName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            
            // Finder den fulde sti inde i DLL'en baseret på filnavnet
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resourceName))
                throw new FileNotFoundException($"Ressourcen '{fileName}' blev ikke fundet i library'et. Husk at sætte Build Action til 'Embedded Resource'.");

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}