using System.Numerics;
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
    /// Like <see cref="CallPaidToolAsync(McpClient, string, IReadOnlyDictionary{string, object?}?, IX402WalletV2, CancellationToken)"/>,
    /// but refuses, before signing anything, a payment over <paramref name="limiter"/>'s caps.
    /// <paramref name="serverKey"/> names the server for the per-server cap and must come from your own config (the endpoint you chose), never from the server's self-reported name.
    /// </summary>
    /// <exception cref="X402PaymentException">No requirement the wallet may pay, or the payment is over a cap.</exception>
    public static async Task<CallToolResult> CallPaidToolAsync(
        this McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        IX402WalletV2 wallet,
        X402SpendLimiter limiter,
        string serverKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        var first = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        if (first.GetPaymentRequired() is not { } paymentRequired)
        {
            return first;
        }

        var server = serverKey;
        X402SpendLimiter.Reservation? reservation = null;
        JsonObject payment;
        try
        {
            payment = await CreatePaymentAsync(paymentRequired, wallet, cancellationToken, (requirement) =>
            {
                if (!BigInteger.TryParse(requirement.Amount, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    throw new X402PaymentException($"Server asked for an unreadable amount '{requirement.Amount}'.", paymentRequired);
                }

                if (limiter.Asset is { } asset && !string.Equals(asset, requirement.Asset, StringComparison.OrdinalIgnoreCase))
                {
                    throw new X402PaymentException($"Server asked for asset {requirement.Asset}; this limiter only allows {asset}.", paymentRequired);
                }

                if (limiter.TryReserve(server, amount, out var taken) is { } refusal)
                {
                    throw new X402PaymentException(refusal, paymentRequired);
                }

                reservation = taken;
            });
        }
        catch
        {
            if (reservation is { } taken)
            {
                limiter.Release(taken);
            }

            throw;
        }

        var meta = new JsonObject { [X402McpProtocol.PaymentMetaKey] = payment };
        return await client.CallToolAsync(toolName, arguments, options: new RequestOptions { Meta = meta }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Builds the <c>_meta["x402/payment"]</c> value for a PaymentRequired: the signed payload
    /// with the resource it pays for, in the spec's shape.
    /// </summary>
    public static Task<JsonObject> CreatePaymentAsync(
        PaymentRequiredResponse paymentRequired,
        IX402WalletV2 wallet,
        CancellationToken cancellationToken = default) =>
        CreatePaymentAsync(paymentRequired, wallet, cancellationToken, null);

    private static async Task<JsonObject> CreatePaymentAsync(
        PaymentRequiredResponse paymentRequired,
        IX402WalletV2 wallet,
        CancellationToken cancellationToken,
        Action<PaymentRequirements>? beforeSigning)
    {
        var requirement = await wallet.SelectPaymentAsync(paymentRequired, cancellationToken)
            ?? throw new X402PaymentException(
                $"Wallet on {wallet.Network} has no allowance for any requirement of {paymentRequired.Resource.Url}.",
                paymentRequired);

        beforeSigning?.Invoke(requirement);
        var header = await wallet.CreateHeaderAsync(requirement, cancellationToken);
        header.Payload.Resource = paymentRequired.Resource.Url;

        var payment = JsonSerializer.SerializeToNode(header, X402McpProtocol.JsonOptions)!.AsObject();
        payment["resource"] = JsonSerializer.SerializeToNode(paymentRequired.Resource, X402McpProtocol.JsonOptions);
        return payment;
    }
}
