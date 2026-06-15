using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ProductionReadySetup.Common.ErrorHandling;

namespace ProductionReadySetup.Common.Extensions
{
    public static class ErrorHandlingExtensions
    {
        public static IServiceCollection AddProductionErrorHandling(this IServiceCollection services)
        {
            // Handles unhandled exceptions globally using ASP.NET Core's built-in mechanism.
            services.AddExceptionHandler<GlobalExceptionHandler>();

            // Writes RFC 9457-style application/problem+json responses.
            services.AddProblemDetails();

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var error = Errors.Common.InvalidRequest;

                    var problemDetails = new ValidationProblemDetails(context.ModelState)
                    {
                        Type = error.Type,
                        Title = error.Title,
                        Status = error.Status,
                        Detail = "One or more request validation errors occurred.",
                        Instance = context.HttpContext.Request.Path
                    };

                    problemDetails.Extensions["errorCode"] = error.Code;
                    problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;


                    return new BadRequestObjectResult(problemDetails)
                    {
                        ContentTypes = { "application/problem+json" }
                    };
                };
            });

            return services;
        }


        public static WebApplication UseProductionErrorHandling(this WebApplication app)
        {
            // Keep this early so it catches exceptions from later middleware/controllers.
            app.UseExceptionHandler();

            return app;
        }
    }
}
