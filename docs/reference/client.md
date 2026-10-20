# Client reference

```csharp
using X402.Mcp;
using X402.Mcp.Client;

var wallet = new EVMWallet(privateKey, "eip155:84532", 84532)
{
    AssetAllowances = [new AssetAllowance { Asset = X402McpOptions.BaseSepoliaUsdc, TotalAllowance = 10_000, MaxPerRequestAllowance = 10_000 }],
};

var result = await mcpClient.CallPaidToolAsync("nbu_rate", new Dictionary<string, object?> { ["currency"] = "USD" }, wallet);

var receipt = result.GetPaymentSettlement();   // tx hash, payer, network
var refusal = result.GetPaymentRequired();     // non-null when the server refused the payment
```

`CallPaidToolAsync` pays only when the server asks and only within the wallet's allowances; otherwise it throws
`X402PaymentException`. `EVMWallet` and `AssetAllowance` come from
[michielpost/x402-dotnet](https://github.com/michielpost/x402-dotnet).
