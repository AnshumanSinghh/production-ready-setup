using ProductionReadySetup.Common.ErrorHandling;
using ProductionReadySetup.Common.Exceptions;

namespace ProductionReadySetup.Api.Exceptions
{
    public sealed class BadRequestAppException : AppException
    {
        public BadRequestAppException(
                ErrorDescriptor error,
                string detail
            ) : base(EnsureStatus(error, StatusCodes.Status400BadRequest), detail)
        {            
        }
    }
}
