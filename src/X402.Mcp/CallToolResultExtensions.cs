using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;

namespace X402.Mcp;

/// <summary>Reads and writes the x402 parts of an MCP tool result.</summary>
public static class CallToolResultExtensions
{
    /// <summary>
    /// The PaymentRequired a paid tool returned, or null when the result is not a payment demand.
    /// Reads <c>structuredContent</c> first and falls back to <c>content[0].text</c>, as the spec asks.
    /// </summary>
    public static PaymentRequiredResponse? GetPaymentRequired(this CallToolResult result)
    {
        if (result.IsError != true)
        {
            return null;
        }

        if (result.StructuredContent is { ValueKind: JsonValueKind.Object } structured
            && TryReadPaymentRequired(structured, out var fromStructured))
        {
            return fromStructured;
        }

        if (result.Content.FirstOrDefault() is TextContentBlock text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text.Text);
                if (TryReadPaymentRequired(doc.RootElement, out var fromText))
                {
                    return fromText;
                }
            }
            catch (JsonException)
            {
                // Plain-text tool error, not a payment demand.
            }
        }

        return null;
    }

    /// <summary>The settlement a paid call produced, or null when the call was free or not settled.</summary>
    public static SettlementResponse? GetPaymentSettlement(this CallToolResult result) =>
        result.Meta?[X402McpProtocol.PaymentResponseMetaKey]?.Deserialize<SettlementResponse>(X402McpProtocol.JsonOptions);

    internal static CallToolResult CreatePaymentRequired(PaymentRequiredResponse paymentRequired)
    {
        var element = JsonSerializer.SerializeToElement(paymentRequired, X402McpProtocol.JsonOptions);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = element,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
        };
    }

    internal static void SetPaymentSettlement(this CallToolResult result, SettlementResponse settlement)
    {
        result.Meta ??= new JsonObject();
        result.Meta[X402McpProtocol.PaymentResponseMetaKey] =
            JsonSerializer.SerializeToNode(settlement, X402McpProtocol.JsonOptions);
    }

    private static bool TryReadPaymentRequired(JsonElement element, out PaymentRequiredResponse? paymentRequired)
    {
        paymentRequired = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("x402Version", out _)
            || !element.TryGetProperty("accepts", out _))
        {
            return false;
        }

        paymentRequired = element.Deserialize<PaymentRequiredResponse>(X402McpProtocol.JsonOptions);
        return paymentRequired is not null;
    }
}
