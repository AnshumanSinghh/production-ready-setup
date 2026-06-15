using ProductionReadySetup.Common.ErrorHandling;
using ProductionReadySetup.Common.Exceptions;

namespace ProductionReadySetup.Api.Exceptions
{
    public sealed class NotFoundException : AppException
    {
        public NotFoundException(
                ErrorDescriptor error,
                Object key
            ) : base(EnsureStatus(error, StatusCodes.Status404NotFound), $"{error.Title} Identifier: '{key}'.")
        {            
        }
    }
}
