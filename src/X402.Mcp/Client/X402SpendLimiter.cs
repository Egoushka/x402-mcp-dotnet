using System.Numerics;

namespace X402.Mcp.Client;

/// <summary>Spending caps in the asset's smallest unit (USDC: 6 decimals, so 10000 = 0.01 USDC). Null means no cap.</summary>
public sealed class X402SpendLimits
{
    /// <summary>Most one payment may cost.</summary>
    public BigInteger? PerCall { get; init; }

    /// <summary>Most all payments may cost in any rolling 24 hours.</summary>
    public BigInteger? PerDay { get; init; }

    /// <summary>Most all payments to one server may cost over the limiter's lifetime.</summary>
    public BigInteger? PerServer { get; init; }
}

/// <summary>
/// Tracks what an agent has spent and refuses payments over <see cref="X402SpendLimits"/>. Counts payments signed, not
/// settled, so it errs on the side of refusing. Amounts are in one asset: use one limiter per asset. State is in
/// memory and resets when the process restarts. Thread safe.
/// </summary>
public sealed class X402SpendLimiter(X402SpendLimits limits, TimeProvider? clock = null)
{
    /// <summary>A payment counted against the caps; hand it to <see cref="Release"/> if the payment is never sent.</summary>
    public readonly record struct Reservation(string Server, BigInteger Amount, DateTimeOffset At);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<Reservation> _recent = [];
    private readonly Dictionary<string, BigInteger> _perServer = [];

    /// <summary>Spent in the last 24 hours.</summary>
    public BigInteger SpentToday
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _recent.Aggregate(BigInteger.Zero, (sum, r) => sum + r.Amount);
            }
        }
    }

    /// <summary>Counts a payment against the caps. Returns null when allowed, otherwise why it is refused (nothing is counted).</summary>
    public string? TryReserve(string server, BigInteger amount, out Reservation reservation)
    {
        reservation = default;
        lock (_gate)
        {
            Prune();
            if (limits.PerCall is { } perCall && amount > perCall)
            {
                return $"Payment of {amount} is over the per-call cap of {perCall}.";
            }

            if (limits.PerDay is { } perDay)
            {
                var today = _recent.Aggregate(BigInteger.Zero, (sum, r) => sum + r.Amount);
                if (today + amount > perDay)
                {
                    return $"Payment of {amount} would take today's spend from {today} over the per-day cap of {perDay}.";
                }
            }

            _perServer.TryGetValue(server, out var spent);
            if (limits.PerServer is { } perServer && spent + amount > perServer)
            {
                return $"Payment of {amount} would take spend on '{server}' from {spent} over the per-server cap of {perServer}.";
            }

            reservation = new Reservation(server, amount, _clock.GetUtcNow());
            _recent.Add(reservation);
            _perServer[server] = spent + amount;
            return null;
        }
    }

    /// <summary>Gives back a reservation whose payment was never sent.</summary>
    public void Release(Reservation reservation)
    {
        lock (_gate)
        {
            if (_recent.Remove(reservation))
            {
                _perServer[reservation.Server] -= reservation.Amount;
            }
        }
    }

    private void Prune()
    {
        var cutoff = _clock.GetUtcNow() - Day;
        _recent.RemoveAll(r => r.At <= cutoff);
    }
}
