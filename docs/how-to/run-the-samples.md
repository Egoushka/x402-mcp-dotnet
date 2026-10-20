# Run the samples against the testnet

The server sample needs an address to receive payments:

```bash
dotnet run --project samples/PaidMcpServer --urls http://localhost:5000 --X402:PayTo=0xYourAddress
```

The agent sample needs a Base Sepolia test key in `X402_PRIVATE_KEY`:

```bash
X402_PRIVATE_KEY=<base sepolia test key> dotnet run --project samples/PayingAgent -- http://localhost:5000/mcp USD
```

Fund the test key with Base Sepolia USDC from [faucet.circle.com](https://faucet.circle.com). An empty wallet gets
`Payment refused: invalid_exact_evm_insufficient_balance` from the facilitator, which also shows the signed payload
was accepted as well-formed.

The facilitator defaults to `https://x402.org/facilitator/`; override it with `--X402:FacilitatorUrl=...` and keep
the trailing slash.
