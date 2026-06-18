using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionReadySetup.Messaging.Options;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Connection
{
    /// <summary>
    /// Manages a single, long-lived RabbitMQ connection for the application.
    ///
    /// WHY SINGLETON CONNECTION:
    ///   Creating a TCP connection per operation is expensive and unnecessary.
    ///   RabbitMQ.Client connections are designed for long-lived reuse and
    ///   support creating many lightweight channels from one connection.
    ///
    /// WHY LAZY + THREAD-SAFE INITIALIZATION:
    ///   Multiple components (topology setup, publisher, health check) may
    ///   request the connection concurrently at startup. We ensure only ONE
    ///   connection is created regardless of concurrent callers.
    ///
    /// AUTOMATIC RECOVERY:
    ///   Configured via RabbitMqOptions.AutomaticRecoveryEnabled.
    ///   RabbitMQ.Client handles reconnection internally on network failure —
    ///   we don't need to manually detect and recreate the connection.
    ///
    /// PITFALL: Never create a new IConnection per publish or per consume.
    ///   This class exists specifically to prevent that anti-pattern.
    /// </summary>
    public sealed class RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger) : IAsyncDisposable
    {
        private readonly RabbitMqOptions _options = options.Value;
        private IConnection? _connection;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        /// <summary>
        /// Returns the shared connection, creating it on first call.
        /// Thread-safe — concurrent callers during startup will not create
        /// multiple connections.
        /// </summary>
        public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
        {
            // Fast path — connection already exists and is open.
            if (_connection is { IsOpen: true })
                return _connection;

            await _connectionLock.WaitAsync(ct);
            try
            {
                // Double-check inside lock — another thread may have created
                // the connection while we waited.
                if (_connection is { IsOpen: true })
                    return _connection;

                var factory = new ConnectionFactory 
                { 
                    HostName = _options.HostName,
                    Port = _options.Port,
                    VirtualHost = _options.VirtualHost,
                    UserName = _options.UserName,
                    Password = _options.Password,
                    Ssl = new SslOption { Enabled = _options.UseTls },
                    RequestedHeartbeat = TimeSpan.FromSeconds(_options.HeartbeatSeconds),
                    AutomaticRecoveryEnabled = _options.AutomaticRecoveryEnabled,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds),

                    // ClientProvidedName shows up in RabbitMQ Management UI —
                    // invaluable for identifying which service owns which connection
                    // when debugging a shared broker with multiple services connected.
                    ClientProvidedName = "production-ready-setup"
                };

                logger.LogInformation(
                    "Creating RabbitMQ connection to {HostName}:{Port}, VirtualHost: {VirtualHost}",
                    _options.HostName, _options.Port, _options.VirtualHost);

                _connection = await factory.CreateConnectionAsync(ct);

                // Log unexpected connection shutdown — helps correlate
                // application errors with broker-side events (restart, network issue).
                _connection.ConnectionShutdownAsync += (_, args) =>
                {
                    logger.LogWarning(
                        "RabbitMQ connection shut down. Reason: {Reason}", args.ReplyText);
                    return Task.CompletedTask;
                };

                return _connection;
            }
            finally
            {

                _connectionLock.Release();
            }
        }


        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                logger.LogInformation("Closing RabbitMQ connection.");
                await _connection.CloseAsync();
                _connection.Dispose();
            }

            _connectionLock.Dispose();
        }
    }
}
