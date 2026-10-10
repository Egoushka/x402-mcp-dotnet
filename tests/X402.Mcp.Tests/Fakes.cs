using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;
using x402.Core.Models.v2.Facilitator;
using x402.Facilitator;

namespace X402.Mcp.Tests;

/// <summary>Facilitator that answers from settable delegates and records what it was asked.</summary>
public sealed class FakeFacilitator : IFacilitatorV2Client
{
    public Func<PaymentPayloadHeader, PaymentRequirements, VerificationResponse> Verify { get; set; } =
        (payload, _) => new VerificationResponse { IsValid = true, Payer = payload.Payload.Authorization.From };

    public Func<PaymentPayloadHeader, PaymentRequirements, SettlementResponse> Settle { get; set; } =
        (payload, requirements) => new SettlementResponse
        {
            Success = true,
            Transaction = "0xfeedface",
            Network = requirements.Network,
            Payer = payload.Payload.Authorization.From,
        };

    public List<PaymentPayloadHeader> Verified { get; } = [];
    public List<PaymentPayloadHeader> Settled { get; } = [];

    public Task<VerificationResponse> VerifyAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, CancellationToken cancellationToken = default)
    {
        Verified.Add(paymentPayload);
        return Task.FromResult(Verify(paymentPayload, requirements));
    }

    public Task<SettlementResponse> SettleAsync(PaymentPayloadHeader paymentPayload, PaymentRequirements requirements, string? settlementAmount = null, CancellationToken cancellationToken = default)
    {
        Settled.Add(paymentPayload);
        return Task.FromResult(Settle(paymentPayload, requirements));
    }

    public Task<SupportedResponse> SupportedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DiscoveryResponse> DiscoveryAsync(string? type = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MerchantDiscoveryResponse> DiscoveryMerchantAsync(string payTo, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DiscoverySearchResponse> DiscoverySearchAsync(DiscoverySearchRequest searchRequest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public sealed class ToolCalls
{
    public int Count;
}

[McpServerToolType]
public sealed class TestTools
{
    [McpServerTool(Name = "echo"), Description("Free echo.")]
    public static string Echo(string text) => text;

    [McpServerTool(Name = "forecast"), Description("Weather forecast for a city.")]
    [PaidTool("10000")]
    public static string Forecast(string city, ToolCalls calls)
    {
        Interlocked.Increment(ref calls.Count);
        return $"Sunny in {city}";
    }

    [McpServerTool(Name = "quote"), Description("Same price as forecast.")]
    [PaidTool("10000")]
    public static string Quote(ToolCalls calls)
    {
        Interlocked.Increment(ref calls.Count);
        return "Buy low";
    }

    [McpServerTool(Name = "broken"), Description("Always fails.")]
    [PaidTool("10000")]
    public static string Broken(ToolCalls calls)
    {
        Interlocked.Increment(ref calls.Count);
        throw new InvalidOperationException("upstream down");
    }

    [McpServerTool(Name = "lookup"), Description("Reports a miss as an error result.")]
    [PaidTool("10000")]
    public static CallToolResult Lookup(ToolCalls calls)
    {
        Interlocked.Increment(ref calls.Count);
        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "not found" }] };
    }
}
