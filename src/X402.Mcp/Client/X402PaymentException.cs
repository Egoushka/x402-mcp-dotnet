using x402.Core.Models.v2;

namespace X402.Mcp.Client;

/// <summary>The client could not pay for a tool the server asked payment for.</summary>
public sealed class X402PaymentException(string message, PaymentRequiredResponse paymentRequired) : Exception(message)
{
    /// <summary>The server's payment demand.</summary>
    public PaymentRequiredResponse PaymentRequired { get; } = paymentRequired;
}
