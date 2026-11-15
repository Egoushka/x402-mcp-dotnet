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

## Spend caps

Wallet allowances are per wallet. For caps that live in your agent, pass an `X402SpendLimiter`:

```csharp
var limiter = new X402SpendLimiter(new X402SpendLimits
{
    PerCall = 10_000,      // smallest asset unit; USDC has 6 decimals, so 0.01 USDC
    PerDay = 500_000,      // rolling 24 hours
    PerServer = 2_000_000, // lifetime of the limiter, per serverKey
});

var result = await mcpClient.CallPaidToolAsync("nbu_rate", args, wallet, limiter, "nbu");
```

A payment over any cap throws `X402PaymentException` before anything is signed. Take `serverKey` from your own config,
not from the server's self-reported name, or a hostile server can dodge its cap. Set `Asset` on the limits to refuse other
assets. The cap counts payments signed, not settled: a paid call that fails after signing still counts. State is in
memory and resets when the process restarts.
