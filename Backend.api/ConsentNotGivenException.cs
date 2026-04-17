using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api
{
    public class ConsentNotGivenException : Exception
    {
        // Default constructor
        public ConsentNotGivenException() 
            : base("Consent has not been provided for this action.") { }

        // Constructor that takes a custom message (e.g., "Consent not given for file {id}")
        public ConsentNotGivenException(string message) 
            : base(message) { }

        // Constructor for inner exceptions (useful for logging/debugging)
        public ConsentNotGivenException(string message, Exception inner) 
            : base(message, inner) { }
    }
}