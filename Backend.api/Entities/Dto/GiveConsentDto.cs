using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    /// <summary>
    /// Consent metadata stored alongside a file upload.
    /// </summary>
    public class GiveConsentDto
    {
        /// <summary>
        /// Indicates whether the uploader granted consent for the file to be stored and used.
        /// </summary>
        public bool ConsentGiven { get; set; }

        /// <summary>
        /// Timestamp recorded for when the consent decision was made.
        /// </summary>
        public DateTime TimeOfConsent { get; set; }
    }
}