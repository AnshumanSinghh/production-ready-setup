using ProductionReadySetup.Common.ErrorHandling;

namespace ProductionReadySetup.Common.Exceptions
{
    public abstract class AppException : Exception
    {
        protected AppException(
            ErrorDescriptor error,
            string detail,
            Exception? innerException = null
            ) : base(detail, innerException)
        {
            // ErrorDescriptor is the API contract: code/title/status/type stay together.
            Error = error;
        }

        // Stable machine-readable code for frontend, logs, alerts, and docs.
        // Do not make clients parse the human-readable message.
        public ErrorDescriptor Error { get; }

        protected static ErrorDescriptor EnsureStatus(ErrorDescriptor error, int expectedStatus)
        {
            if (error.Status != expectedStatus)
            {
                throw new InvalidOperationException(
                    $"Error '{error.Code}' must use HTTP {expectedStatus}, but found {error.Status}.");
            }

            return error;
        }

    }
}
