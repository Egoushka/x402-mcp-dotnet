# Server reference

```csharp
using x402;

builder.Services.WithHttpFacilitator("https://x402.org/facilitator/"); // testnet facilitator; keep the trailing slash
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<RateTools>()
    .WithX402Payments(o => o.PayTo = "0xYourAddress");

[McpServerToolType]
public sealed class RateTools
{
    [McpServerTool(Name = "nbu_rate"), Description("Official NBU exchange rate to UAH.")]
    [PaidTool("1000")] // atomic units: 1000 = 0.001 USDC
    public static Task<string> NbuRate(string currency) => ...;
}
```

Tools without `[PaidTool]` stay free.

## `PaidToolAttribute`

Goes on a tool method or on a whole tool class. Unset properties fall back to `X402McpOptions`.

| Property | Meaning |
|---|---|
| `Amount` (constructor) | Price per call in atomic units of the asset; `"10000"` is 0.01 USDC. |
| `Asset` | Token contract address. |
| `PayTo` | Address that receives the payment. |
| `Network` | CAIP-2 network id, for example `eip155:8453`. Defaults to the asset's known network. |
| `Description` | What the payer is buying. Defaults to the tool's own description. |

## `X402McpOptions`

| Property | Default |
|---|---|
| `PayTo` | none; set it here or on every tool |
| `Asset` | `X402McpOptions.BaseSepoliaUsdc`, USDC on Base Sepolia (the x402 testnet) |
| `Network` | from the asset's known network |
| `MaxTimeoutSeconds` | 60: how long a signed authorization may take to arrive |
