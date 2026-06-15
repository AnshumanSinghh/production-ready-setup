using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProductionReadySetup.Common.Exceptions;

namespace ProductionReadySetup.Common.ErrorHandling
{
    public static class ProblemDetailsExtensions
    {
        public static ProblemDetails ToProblemDetails(
            this AppException exception,
            HttpContext httpContext)
        {
            var problemDetails = new ProblemDetails
            {
                Type = exception.Error.Type,
                Title = exception.Error.Title,
                Status = exception.Error.Status,
                Detail = exception.Message,
                Instance = httpContext.Request.Path
            };

            // errorCode is for clients. traceId is for support/log correlation.
            problemDetails.Extensions["errorCode"] = exception.Error.Code;
            problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

            // Getting Error: CS0246  - How to fix, strictly aligned with Industry Standards
            if (exception is ValidationAppException validationException)
            {
                problemDetails.Extensions["errors"] = validationException.ValidationErrors;
            }
            
            return problemDetails;
        }

        public static ProblemDetails ToUnExpectedProblemDetials(HttpContext httpContext)
        {
            var error = Errors.Common.Unexpected;

            var problemDetails = new ProblemDetails
            {
                Type = error.Type,
                Title = error.Title,
                Status = error.Status,


                // Never expose internal exception details to API clients.
                Detail = "The server encountered an unexpected error.",
                Instance = httpContext.Request.Path
            };

            problemDetails.Extensions["errorCode"] = error.Code;
            problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

            return problemDetails;
        }
    }
}
