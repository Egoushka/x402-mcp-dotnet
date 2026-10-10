using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using x402.Client.v2;
using x402.Core.Models.v2;

namespace X402.Mcp.Client;

/// <summary>Calls MCP tools and pays for them when the server asks.</summary>
public static class X402McpClientExtensions
{
    /// <summary>
    /// Calls a tool; if it answers with an x402 PaymentRequired, signs a payment with
    /// <paramref name="wallet"/> (within its allowances) and calls again with the payment attached.
    /// Free tools cost one round trip, paid tools two. Read the receipt with
    /// <see cref="CallToolResultExtensions.GetPaymentSettlement"/>.
    /// </summary>
    /// <exception cref="X402PaymentException">No requirement the wallet is allowed to pay.</exception>
    public static async Task<CallToolResult> CallPaidToolAsync(
        this McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        IX402WalletV2 wallet,
        CancellationToken cancellationToken = default)
    {
        var first = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        if (first.GetPaymentRequired() is not { } paymentRequired)
        {
            return first;
        }

        var meta = new JsonObject
        {
            [X402McpProtocol.PaymentMetaKey] = await CreatePaymentAsync(paymentRequired, wallet, cancellationToken),
        };
        return await client.CallToolAsync(toolName, arguments, options: new RequestOptions { Meta = meta }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Builds the <c>_meta["x402/payment"]</c> value for a PaymentRequired: the signed payload
    /// with the resource it pays for, in the spec's shape.
    /// </summary>
    public static async Task<JsonObject> CreatePaymentAsync(
        PaymentRequiredResponse paymentRequired,
        IX402WalletV2 wallet,
        CancellationToken cancellationToken = default)
    {
        var requirement = await wallet.SelectPaymentAsync(paymentRequired, cancellationToken)
            ?? throw new X402PaymentException(
                $"Wallet on {wallet.Network} has no allowance for any requirement of {paymentRequired.Resource.Url}.",
                paymentRequired);

        var header = await wallet.CreateHeaderAsync(requirement, cancellationToken);
        header.Payload.Resource = paymentRequired.Resource.Url;

        var payment = JsonSerializer.SerializeToNode(header, X402McpProtocol.JsonOptions)!.AsObject();
        payment["resource"] = JsonSerializer.SerializeToNode(paymentRequired.Resource, X402McpProtocol.JsonOptions);
        return payment;
    }
}
