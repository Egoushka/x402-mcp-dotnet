using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using x402.Client.EVM;
using x402.Client.Models;
using X402.Mcp;
using X402.Mcp.Client;

// Usage: dotnet run -- [server-url] [currency]
// Needs X402_PRIVATE_KEY: a Base Sepolia test key holding test USDC (https://faucet.circle.com).
var serverUrl = args.ElementAtOrDefault(0) ?? "http://localhost:5000/mcp";
var currency = args.ElementAtOrDefault(1) ?? "USD";
var privateKey = Environment.GetEnvironmentVariable("X402_PRIVATE_KEY")
    ?? throw new InvalidOperationException("Set X402_PRIVATE_KEY to a Base Sepolia test key.");

var wallet = new EVMWallet(privateKey, "eip155:84532", 84532)
{
    // Never pay more than 0.01 USDC per call.
    AssetAllowances = [new AssetAllowance { Asset = X402McpOptions.BaseSepoliaUsdc, TotalAllowance = 10_000, MaxPerRequestAllowance = 10_000 }],
};
Console.WriteLine($"Paying from {wallet.OwnerAddress}");

await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(serverUrl) }));

var unpaid = await client.CallToolAsync("nbu_rate", new Dictionary<string, object?> { ["currency"] = currency });
if (unpaid.GetPaymentRequired() is { } demand)
{
    var price = demand.Accepts[0];
    Console.WriteLine($"Server asks {price.Amount} atomic units of {price.Extra?.Name ?? price.Asset} on {price.Network} for {demand.Resource.Url}");
}

var result = await client.CallPaidToolAsync("nbu_rate", new Dictionary<string, object?> { ["currency"] = currency }, wallet);
if (result.GetPaymentRequired() is { } refused)
{
    Console.WriteLine($"Payment refused: {refused.Error}");
    return 1;
}

Console.WriteLine(string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
if (result.GetPaymentSettlement() is { } receipt)
{
    Console.WriteLine($"Paid: https://sepolia.basescan.org/tx/{receipt.Transaction}");
}

return 0;
