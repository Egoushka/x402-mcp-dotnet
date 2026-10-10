using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using x402.Client.EVM;
using x402.Client.Models;
using x402.Core.Models.Facilitator;
using X402.Mcp.Client;

namespace X402.Mcp.Tests;

public sealed class PaidToolTests : IAsyncLifetime
{
    private const string BaseSepolia = "eip155:84532";
    private readonly McpServerFixture _server = new();
    private readonly EVMWallet _wallet = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), BaseSepolia, 84532) { IgnoreAllowances = true };
    private ModelContextProtocol.Client.McpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Dictionary<string, object?> Kyiv => new() { ["city"] = "Kyiv" };

    public async ValueTask InitializeAsync() => _client = await _server.ConnectAsync();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Free_tool_runs_without_payment()
    {
        var result = await _client.CallToolAsync("echo", new Dictionary<string, object?> { ["text"] = "hi" }, cancellationToken: Ct);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("hi", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Empty(_server.Facilitator.Verified);
    }

    [Fact]
    public async Task Paid_tool_without_payment_returns_spec_shaped_payment_required()
    {
        var result = await _client.CallToolAsync("forecast", Kyiv, cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Equal(0, _server.ToolCalls.Count);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(2, structured.GetProperty("x402Version").GetInt32());
        Assert.Equal("Payment required to access this resource", structured.GetProperty("error").GetString());
        Assert.Equal("mcp://tool/forecast", structured.GetProperty("resource").GetProperty("url").GetString());
        Assert.Equal("Weather forecast for a city.", structured.GetProperty("resource").GetProperty("description").GetString());

        var accepts = Assert.Single(structured.GetProperty("accepts").EnumerateArray());
        Assert.Equal("exact", accepts.GetProperty("scheme").GetString());
        Assert.Equal(BaseSepolia, accepts.GetProperty("network").GetString());
        Assert.Equal("10000", accepts.GetProperty("amount").GetString());
        Assert.Equal(X402McpOptions.BaseSepoliaUsdc, accepts.GetProperty("asset").GetString());
        Assert.Equal(McpServerFixture.PayTo, accepts.GetProperty("payTo").GetString());
        Assert.Equal("USDC", accepts.GetProperty("extra").GetProperty("name").GetString());

        // content[0].text carries the same object for clients without structured content.
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(structured.GetRawText()), JsonNode.Parse(text)));

        var parsed = result.GetPaymentRequired();
        Assert.NotNull(parsed);
        Assert.Equal("mcp://tool/forecast", parsed.Resource.Url);
    }

    [Fact]
    public async Task Paying_client_gets_tool_result_and_settlement()
    {
        var result = await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, Ct);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Sunny in Kyiv", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(1, _server.ToolCalls.Count);

        var settlement = result.GetPaymentSettlement();
        Assert.NotNull(settlement);
        Assert.True(settlement.Success);
        Assert.Equal("0xfeedface", settlement.Transaction);
        Assert.Equal(_wallet.OwnerAddress, settlement.Payer, ignoreCase: true);

        var paid = Assert.Single(_server.Facilitator.Settled);
        Assert.Equal("10000", paid.Payload.Authorization.Value);
        Assert.Equal(McpServerFixture.PayTo, paid.Payload.Authorization.To, ignoreCase: true);
        Assert.StartsWith("0x", paid.Payload.Signature);
    }

    [Fact]
    public async Task Payment_meta_has_spec_shape()
    {
        var paymentRequired = (await _client.CallToolAsync("forecast", Kyiv, cancellationToken: Ct)).GetPaymentRequired()!;

        var payment = await X402McpClientExtensions.CreatePaymentAsync(paymentRequired, _wallet, Ct);

        Assert.Equal(2, payment["x402Version"]!.GetValue<int>());
        Assert.Equal("mcp://tool/forecast", payment["resource"]!["url"]!.GetValue<string>());
        Assert.Equal("10000", payment["accepted"]!["amount"]!.GetValue<string>());
        Assert.NotNull(payment["payload"]!["signature"]);
        Assert.NotNull(payment["payload"]!["authorization"]!["nonce"]);
    }

    [Fact]
    public async Task Rejected_verification_returns_reason_and_does_not_run_tool()
    {
        _server.Facilitator.Verify = (_, _) => new() { IsValid = false, InvalidReason = "invalid_exact_evm_insufficient_balance" };

        var result = await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, Ct);

        Assert.Equal("invalid_exact_evm_insufficient_balance", result.GetPaymentRequired()?.Error);
        Assert.Equal(0, _server.ToolCalls.Count);
        Assert.Empty(_server.Facilitator.Settled);
    }

    [Theory]
    [InlineData("broken")] // throws
    [InlineData("lookup")] // returns isError: true
    public async Task Failed_tool_is_not_charged(string tool)
    {
        var result = await _client.CallPaidToolAsync(tool, null, _wallet, Ct);

        Assert.True(result.IsError);
        Assert.Null(result.GetPaymentRequired());
        Assert.Equal(1, _server.ToolCalls.Count);
        Assert.Single(_server.Facilitator.Verified);
        Assert.Empty(_server.Facilitator.Settled);
    }

    [Fact]
    public async Task Failed_settlement_withholds_tool_output()
    {
        _server.Facilitator.Settle = (_, _) => new SettlementResponse { Success = false, ErrorReason = "nonce_already_used" };

        var result = await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, Ct);

        Assert.Equal("Settlement failed: nonce_already_used", result.GetPaymentRequired()?.Error);
        Assert.DoesNotContain(result.Content, c => c is TextContentBlock { Text: "Sunny in Kyiv" });
        Assert.Null(result.GetPaymentSettlement());
    }

    [Fact]
    public async Task Payment_for_one_tool_is_refused_by_another()
    {
        var forecastDemand = (await _client.CallToolAsync("forecast", Kyiv, cancellationToken: Ct)).GetPaymentRequired()!;
        var forecastPayment = await X402McpClientExtensions.CreatePaymentAsync(forecastDemand, _wallet, Ct);

        var result = await _client.CallToolAsync("quote", null,
            options: new RequestOptions { Meta = new JsonObject { [X402McpProtocol.PaymentMetaKey] = forecastPayment } },
            cancellationToken: Ct);

        Assert.Equal("Payment payload resource does not match the requested tool", result.GetPaymentRequired()?.Error);
        Assert.Empty(_server.Facilitator.Verified);
        Assert.Equal(0, _server.ToolCalls.Count);
    }

    [Fact]
    public async Task Underpaying_authorization_is_refused()
    {
        var demand = (await _client.CallToolAsync("forecast", Kyiv, cancellationToken: Ct)).GetPaymentRequired()!;
        demand.Accepts[0].Amount = "1";
        var cheapPayment = await X402McpClientExtensions.CreatePaymentAsync(demand, _wallet, Ct);

        var result = await _client.CallToolAsync("forecast", Kyiv,
            options: new RequestOptions { Meta = new JsonObject { [X402McpProtocol.PaymentMetaKey] = cheapPayment } },
            cancellationToken: Ct);

        Assert.Equal("No matching payment requirements found for the provided payload", result.GetPaymentRequired()?.Error);
        Assert.Empty(_server.Facilitator.Verified);
    }

    [Fact]
    public async Task Malformed_payment_is_refused()
    {
        var result = await _client.CallToolAsync("forecast", Kyiv,
            options: new RequestOptions { Meta = new JsonObject { [X402McpProtocol.PaymentMetaKey] = "not a payment" } },
            cancellationToken: Ct);

        Assert.Equal("Malformed x402 payment payload", result.GetPaymentRequired()?.Error);
    }

    [Fact]
    public async Task Wallet_without_allowance_throws_instead_of_paying()
    {
        var stingy = new EVMWallet(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), BaseSepolia, 84532)
        {
            AssetAllowances = [new AssetAllowance { Asset = X402McpOptions.BaseSepoliaUsdc, TotalAllowance = 5000, MaxPerRequestAllowance = 5000 }],
        };

        var ex = await Assert.ThrowsAsync<X402PaymentException>(() => _client.CallPaidToolAsync("forecast", Kyiv, stingy, Ct));

        Assert.Equal("mcp://tool/forecast", ex.PaymentRequired.Resource.Url);
        Assert.Empty(_server.Facilitator.Verified);
    }
}
