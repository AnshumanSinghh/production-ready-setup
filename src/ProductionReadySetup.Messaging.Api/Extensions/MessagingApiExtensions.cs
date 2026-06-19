using ProductionReadySetup.Messaging.Api.Services;

namespace ProductionReadySetup.Messaging.Api.Extensions
{
    /// <summary>
    /// Registers Messaging.Api-specific services on top of the shared
    /// RabbitMQ infrastructure registered by AddProductionRabbitMq().
    ///
    /// CALL ORDER IN Program.cs:
    ///   builder.Services.AddProductionRabbitMq();          ← shared infra (Messaging project)
    ///   builder.Services.AddProductionMessagingPublisher(); ← Api-specific services (this)
    ///
    /// WHAT THIS REGISTERS:
    ///   IOrderPublisher → OrderPublisher
    ///   (IMessagePublisher and RabbitMQ infrastructure are registered
    ///    by AddProductionRabbitMq() — not duplicated here)
    /// </summary>
    public static class MessagingApiExtensions
    {
        public static IServiceCollection AddProductionMessagingPublisher(this IServiceCollection services) 
        {
            // OrderPublisher is scoped — it handles one HTTP request's
            // worth of publishing. Singleton would also work (no state),
            // but Scoped aligns with the request lifecycle and is safer
            // if state is ever added in future iterations.
            services.AddScoped<IOrderPublisher, OrderPublisher>();

            return services;
        }
    }
}
