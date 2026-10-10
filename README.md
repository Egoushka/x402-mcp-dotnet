# x402-mcp-dotnet

Paid MCP tools for .NET. Put `[PaidTool("10000")]` on a tool, and an AI agent pays 0.01 USDC per call before the tool runs.

It implements the [x402 v2 MCP transport](https://github.com/coinbase/x402/blob/main/specs/transports-v2/mcp.md) for the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), on top of [michielpost/x402-dotnet](https://github.com/michielpost/x402-dotnet) for the protocol types, the facilitator client and the EVM wallet. Other .NET x402 libraries cover HTTP endpoints; this one covers MCP tool calls.

## How a paid call works

```
agent                         MCP server                         facilitator
  │ tools/call nbu_rate            │                                   │
  │───────────────────────────────>│                                   │
  │ isError: true                  │                                   │
  │ structuredContent: PaymentRequired (price, asset, payTo)           │
  │<───────────────────────────────│                                   │
  │ sign EIP-3009 authorization    │                                   │
  │ tools/call + _meta["x402/payment"]                                 │
  │───────────────────────────────>│ verify ─────────────────────────> │
  │                                │ run the tool                      │
  │                                │ settle ─────────────────────────> │
  │ result + _meta["x402/payment-response"] (tx hash)                  │
  │<───────────────────────────────│                                   │
```

The server never holds a key. The payer signs a transfer authorization, and the facilitator moves the USDC on chain.

## Server

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

Tools without `[PaidTool]` stay free. `PaidToolAttribute` can override `Asset`, `PayTo`, `Network` and `Description` per tool; `X402McpOptions` holds the defaults (USDC on Base Sepolia).

## Client

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

`CallPaidToolAsync` pays only when the server asks and only within the wallet's allowances; otherwise it throws `X402PaymentException`.

## What the server checks

Before calling the facilitator, it checks that the payment is for this tool (`mcp://tool/{name}`), matches the price, asset, network and recipient exactly, and is inside its validity window. Then:

- verification fails → PaymentRequired with the facilitator's reason, and the tool does not run;
- the tool fails (throws or returns `isError`) → nothing is settled, so the agent is not charged;
- settlement fails → the tool's output is withheld and a PaymentRequired is returned instead.

## Try it

```bash
dotnet test
```

The tests run a real MCP client and server in process, sign with a real EVM key and use a fake facilitator.

To run the sample against the public testnet facilitator:

```bash
dotnet run --project samples/PaidMcpServer --urls http://localhost:5000 --X402:PayTo=0xYourAddress
```

```bash
X402_PRIVATE_KEY=<base-sepolia test key> dotnet run --project samples/PayingAgent -- http://localhost:5000/mcp USD
```

Fund the test key with Base Sepolia USDC from [faucet.circle.com](https://faucet.circle.com). An empty wallet gets `Payment refused: invalid_exact_evm_insufficient_balance` from the facilitator, which also shows the signed payload was accepted as well-formed.

## Limits

- Only the `exact` scheme. `upto` (charge by usage) is the natural next step for LLM-backed tools.
- A tool runs before settlement. If two calls race with the same signed payment, both run, but only the first one settles and gets the output.
- The paying side here is your own agent code using `CallPaidToolAsync`. Whether a given MCP host pays x402 on its own is up to that host.
- Tested against the public x402.org testnet facilitator up to verification, and end to end against x402-dotnet's mock facilitator. No mainnet run yet.

## License

MIT
