namespace X402.Mcp;

/// <summary>
/// Makes an MCP tool paid: a call without a valid x402 payment gets a PaymentRequired result
/// instead of running the tool. Unset properties fall back to <see cref="X402McpOptions"/>.
/// </summary>
/// <param name="amount">Price per call in atomic units of the asset, e.g. "10000" is 0.01 USDC.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class PaidToolAttribute(string amount) : Attribute
{
    /// <summary>Price per call in atomic units of the asset.</summary>
    public string Amount { get; } = amount;

    /// <summary>Token contract address. Defaults to <see cref="X402McpOptions.Asset"/>.</summary>
    public string? Asset { get; set; }

    /// <summary>Address that receives the payment. Defaults to <see cref="X402McpOptions.PayTo"/>.</summary>
    public string? PayTo { get; set; }

    /// <summary>CAIP-2 network id, e.g. "eip155:8453". Defaults to the asset's known network.</summary>
    public string? Network { get; set; }

    /// <summary>What the payer is buying. Defaults to the tool's own description.</summary>
    public string? Description { get; set; }
}
