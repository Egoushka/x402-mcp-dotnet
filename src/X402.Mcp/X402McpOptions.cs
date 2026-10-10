namespace X402.Mcp;

/// <summary>Server-wide defaults for paid tools.</summary>
public sealed class X402McpOptions
{
    /// <summary>USDC on Base Sepolia, the x402 testnet default.</summary>
    public const string BaseSepoliaUsdc = "0x036CbD53842c5426634e7929541eC2318f3dCF7e";

    /// <summary>Address that receives payments unless a tool sets its own.</summary>
    public string? PayTo { get; set; }

    /// <summary>Token contract address unless a tool sets its own.</summary>
    public string Asset { get; set; } = BaseSepoliaUsdc;

    /// <summary>CAIP-2 network id. When null, it comes from the asset's known network.</summary>
    public string? Network { get; set; }

    /// <summary>How long a signed authorization may take to arrive.</summary>
    public int MaxTimeoutSeconds { get; set; } = 60;
}
