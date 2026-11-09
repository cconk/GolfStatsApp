using System;
using System.Collections.Generic;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Request failed validation; keys are request paths (e.g. "golfers[2]").</summary>
    public class PredictionValidationException : Exception
    {
        public PredictionValidationException(IDictionary<string, string[]> errors)
            : base("One or more validation errors occurred.") => Errors = errors;

        public PredictionValidationException(string key, string message)
            : this(new Dictionary<string, string[]> { [key] = new[] { message } }) { }

        public IDictionary<string, string[]> Errors { get; }
    }

    public class PredictionNotFoundException : Exception
    {
        public PredictionNotFoundException(string message) : base(message) { }
    }

    public class PredictionDataUnavailableException : Exception
    {
        public PredictionDataUnavailableException(string message) : base(message) { }
    }
}
