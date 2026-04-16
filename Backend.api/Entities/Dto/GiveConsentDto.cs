using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    public class GiveConsentDto
    {
        public bool ConsentGiven { get; set; }
        public DateTime TimeOfConsent { get; set; }
    }
}