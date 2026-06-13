using ProductionReadySetup.Api.ErrorHandling;

namespace ProductionReadySetup.Api.Exceptions
{
    public sealed class ConflictException : AppException
    {
        public ConflictException(
                ErrorDescriptor error,
                string detail
            ) : base(EnsureStatus(error, StatusCodes.Status409Conflict), detail)
        {            
        }
    }
}
