using Microsoft.AspNetCore.Diagnostics;
using ProductionReadySetup.Api.Exceptions;

namespace ProductionReadySetup.Api.ErrorHandling
{
    public sealed class GlobalExceptionHandler : IExceptionHandler
    {

        public readonly ILogger<GlobalExceptionHandler> _logger;
        public readonly IProblemDetailsService _problemDetailsService;
        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetailsService)
        {
            _logger = logger;
            _problemDetailsService = problemDetailsService;
        }


        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext, 
            Exception exception, 
            CancellationToken cancellationToken)
        {
            if (httpContext.Response.HasStarted)
            {
                _logger.LogError(exception, "Exception occurred after response started.");
                return false;
            }

            var problemDetails = exception switch
            {
                AppException appException => appException.ToProblemDetails(httpContext),
                _ => ProblemDetailsExtensions.ToUnExpectedProblemDetials(httpContext)
            };

            httpContext.Response.StatusCode = 
                problemDetails.Status ?? StatusCodes.Status500InternalServerError;

            httpContext.Response.ContentType = "application/problem+json";

            if (exception is AppException)
            {
                _logger.LogWarning(
                    exception,
                    "Handled application exception. ErrorCode: {ErrorCode}, TraceId: {TraceId}",
                    problemDetails.Extensions["errorCode"],
                    httpContext.TraceIdentifier);
            }
            else 
            {
                _logger.LogError(
                    exception,
                    "Unhandled Exception TraceId: {TraceId}.",
                    httpContext.TraceIdentifier
                    );
            }

            var written = await _problemDetailsService.TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        ProblemDetails = problemDetails,
                        Exception = exception,
                    });


            if (!written)
            {
                await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            }

            return true;
        }
    }
}
