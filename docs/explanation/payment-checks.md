# What the server checks

Before calling the facilitator, the server checks that the payment is for this tool (`mcp://tool/{name}`), matches
the price, asset, network and recipient exactly, and is inside its validity window. Then:

- verification fails → PaymentRequired with the facilitator's reason, and the tool does not run;
- the tool fails (throws or returns `isError`) → nothing is settled, so the agent is not charged;
- settlement fails → the tool's output is withheld and a PaymentRequired is returned instead.

The server never holds a key. The payer signs an EIP-3009 transfer authorization (a signed "move this much USDC
to that address" message), and the facilitator moves the USDC on chain.

## Limits

- Only the `exact` scheme. `upto` (charge by usage) is the natural next step for LLM-backed tools.
- A tool runs before settlement. If two calls race with the same signed payment, both run, but only the first one
  settles and gets the output.
- The paying side is your own agent code using `CallPaidToolAsync`. Whether a given MCP host pays x402 on its own
  is up to that host.
- Tested against the public x402.org testnet facilitator up to verification, and end to end against x402-dotnet's
  mock facilitator. No mainnet run yet.
