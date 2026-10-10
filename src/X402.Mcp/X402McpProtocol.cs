using System.Text.Json;
using System.Text.Json.Serialization;

namespace X402.Mcp;

/// <summary>
/// Constants of the x402 v2 MCP transport
/// (https://github.com/coinbase/x402/blob/main/specs/transports-v2/mcp.md).
/// </summary>
public static class X402McpProtocol
{
    /// <summary>The x402 protocol version this package speaks.</summary>
    public const int Version = 2;

    /// <summary><c>_meta</c> key of a tools/call request that carries the PaymentPayload.</summary>
    public const string PaymentMetaKey = "x402/payment";

    /// <summary><c>_meta</c> key of a tool result that carries the SettlementResponse.</summary>
    public const string PaymentResponseMetaKey = "x402/payment-response";

    /// <summary>Scheme of the resource URL a paid tool is priced under: <c>mcp://tool/{name}</c>.</summary>
    public const string ToolResourcePrefix = "mcp://tool/";

    // Same shape the x402 package writes on the HTTP transport: camelCase, nulls omitted.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
