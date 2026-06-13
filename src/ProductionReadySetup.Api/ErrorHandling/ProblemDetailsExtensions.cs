using Microsoft.AspNetCore.Mvc;
using ProductionReadySetup.Api.Exceptions;

namespace ProductionReadySetup.Api.ErrorHandling
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
