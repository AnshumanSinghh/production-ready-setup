using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductionReadySetup.Messaging.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.HealthChecks
{
    /// <summary>
    /// Health check for RabbitMQ connectivity.
    ///
    /// CHECK DEPTH — connection-level only:
    ///   We verify IConnection.IsOpen == true.
    ///   We do NOT publish a test message (side effects, unnecessary cost)
    ///   and we do NOT check queue depth (that's a metrics/alerting concern,
    ///   not a binary healthy/unhealthy signal).
    ///
    /// USAGE:
    ///   Registered under "infrastructure" tag — included in /health/ready,
    ///   excluded from /health/live (see Program.cs in Messaging.Api / Worker).
    ///   WHY: RabbitMQ being down means the app is NOT READY to process
    ///   messages, but the PROCESS ITSELF is still alive — Kubernetes should
    ///   stop routing traffic, not restart the pod.
    /// </summary>
    public sealed class RabbitMqHealthCheck(
        RabbitMqConnectionProvider rabbitMqConnectionProvider) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connection = await rabbitMqConnectionProvider.GetConnectionAsync(cancellationToken);

                return connection.IsOpen ?
                    HealthCheckResult.Healthy("RabbitMQ connection is open.")
                    : HealthCheckResult.Unhealthy("RabbitMQ connection is not open.");
            }
            catch (Exception ex)
            {
                // Do not leak internal exception details in the health check
                // description if it's ever exposed externally — keep it generic,
                // full exception is available in logs via the health check's
                // built-in exception logging in ASP.NET Core.
                return HealthCheckResult.Unhealthy(
                    "RabbitMQ connection check failed.", ex);
            }
        }
    }
}
