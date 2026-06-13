using ProductionReadySetup.Api.ErrorHandling;

namespace ProductionReadySetup.Api.Exceptions
{
    public sealed class ValidationAppException : AppException
    {
        public ValidationAppException(
                IDictionary<string, string[]> errors
            ) : base(
                    Errors.Common.ValidationFailed,
                    "One or more business validation errors occurred."
                )
        {
            ValidationErrors = errors;
        }

        public IDictionary<string, string[]> ValidationErrors { get; }
    }
}
