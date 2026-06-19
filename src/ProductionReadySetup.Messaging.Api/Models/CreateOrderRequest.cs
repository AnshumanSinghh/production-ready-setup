using System.ComponentModel.DataAnnotations;

namespace ProductionReadySetup.Messaging.Api.Models
{
    /// <summary>
    /// HTTP request model for creating an order via the API.
    ///
    /// WHY SEPARATE FROM OrderCreatedEvent:
    ///   CreateOrderRequest  = what the CLIENT sends to US (HTTP contract)
    ///   OrderCreatedEvent   = what WE send to the WORKER (messaging contract)
    ///   These are deliberately separate — HTTP contract and message contract
    ///   can evolve independently. A new API field doesn't necessarily mean
    ///   a new message field and vice versa.
    ///
    /// PITFALL: Never expose your internal message contracts directly as API
    ///   request/response models. It couples your HTTP API to your messaging
    ///   schema — changing one forces changing the other.
    /// </summary>
    public sealed class CreateOrderRequest
    {
        /// <summary>
        /// Identifier of the customer placing the order.
        /// </summary>
        [Required]
        public string CustomerId { get; init; } = string.Empty;

        /// <summary>
        /// Line items in the order. At least one item required.
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "Order must contain at least one item.")]
        public IReadOnlyList<CreateOrderItemRequest> Items { get; init; } = [];

        /// <summary>
        /// ISO 4217 currency code. Defaults to USD.
        /// </summary>
        public string Currency { get; init; } = "USD";
    }

    /// <summary>
    /// Represents a single line item in the order request.
    /// </summary>
    public sealed class CreateOrderItemRequest
    {
        [Required]
        public string ProductId { get; init; } = string.Empty;

        [Required]
        public string ProductName { get; init; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
        public int Quantity { get; init; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Unit price must be greater than 0.")]
        public decimal UnitPrice { get; init; }
    }
}
