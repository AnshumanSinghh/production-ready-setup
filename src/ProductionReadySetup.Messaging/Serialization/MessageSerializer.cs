using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProductionReadySetup.Messaging.Serialization
{
    /// <summary>
    /// Centralized JSON serialization for all RabbitMQ message bodies.
    ///
    /// WHY CENTRALIZED:
    ///   Publisher and consumer MUST use identical serialization settings.
    ///   If publisher uses camelCase and consumer expects PascalCase,
    ///   deserialization silently produces default values — a painful bug
    ///   to track down. One shared serializer eliminates this entire class of bugs.
    ///
    /// SAFETY:
    ///   No dynamic/object deserialization anywhere — generic methods only.
    ///   Matches the same safety rule applied in RedisAppCache (Track 3).
    /// </summary>
    public class MessageSerializer
    {
        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false // compact wire format — every byte matters on the network
        };

        /// <summary>
        /// Serializes a message object to UTF-8 bytes for the RabbitMQ message body.
        /// </summary>
        public static byte[] Serialize<T>(T value)
        {
            var json = JsonSerializer.Serialize(value, jsonOptions);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Deserializes RabbitMQ message body bytes back to a typed object.
        /// Returns null if the body is corrupt or does not match T's shape.
        ///
        /// PITFALL: Caller must treat null as a poison message — never
        /// requeue indefinitely on deserialization failure. Route to DLQ.
        /// </summary>
        public static T? Deserialize<T>(ReadOnlyMemory<byte> body)
        {
            try
            {
                var json = Encoding.UTF8.GetString(body.Span);
                return JsonSerializer.Deserialize<T>(json, jsonOptions);

            }
            catch (JsonException)
            {
                return default;
            }
        }
    }
}
