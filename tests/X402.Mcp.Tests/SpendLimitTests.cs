using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using x402.Client.EVM;
using X402.Mcp.Client;

namespace X402.Mcp.Tests;

public sealed class SpendLimitTests : IAsyncLifetime
{
    private readonly McpServerFixture _server = new();
    private readonly EVMWallet _wallet = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "eip155:84532", 84532) { IgnoreAllowances = true };
    private ModelContextProtocol.Client.McpClient _client = null!;
    private static readonly Dictionary<string, object?> Kyiv = new() { ["city"] = "Kyiv" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _client = await _server.ConnectAsync();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Loop_of_100_calls_stops_at_the_per_day_cap()
    {
        var limiter = new X402SpendLimiter(new X402SpendLimits { PerDay = 250_000 });   // 25 calls at 10000

        var paid = 0;
        X402PaymentException? stopped = null;
        for (var i = 0; i < 100; i++)
        {
            try
            {
                await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, cancellationToken: Ct);
                paid++;
            }
            catch (X402PaymentException ex)
            {
                stopped = ex;
                break;
            }
        }

        Assert.Equal(25, paid);
        Assert.NotNull(stopped);
        Assert.Contains("per-day", stopped.Message);
        Assert.Equal(25, _server.Facilitator.Settled.Count);
        Assert.Equal(25, _server.ToolCalls.Count);
    }

    [Fact]
    public async Task Per_call_cap_refuses_before_signing()
    {
        var limiter = new X402SpendLimiter(new X402SpendLimits { PerCall = 9_999 });

        var ex = await Assert.ThrowsAsync<X402PaymentException>(() => _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, cancellationToken: Ct));

        Assert.Contains("per-call", ex.Message);
        Assert.Empty(_server.Facilitator.Verified);
        Assert.Equal(0, _server.ToolCalls.Count);
    }

    [Fact]
    public async Task Per_server_cap_is_total_for_that_server_and_independent_of_others()
    {
        var limiter = new X402SpendLimiter(new X402SpendLimits { PerServer = 20_000 });

        await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, "a", Ct);
        await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, "a", Ct);
        var ex = await Assert.ThrowsAsync<X402PaymentException>(() => _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, "a", Ct));
        Assert.Contains("per-server", ex.Message);

        await _client.CallPaidToolAsync("forecast", Kyiv, _wallet, limiter, "b", Ct);
    }

    [Fact]
    public void Day_window_rolls()
    {
        var clock = new FakeTimeProvider();
        var limiter = new X402SpendLimiter(new X402SpendLimits { PerDay = 100 }, clock);

        Assert.Null(limiter.TryReserve("s", 100, out _));
        Assert.NotNull(limiter.TryReserve("s", 1, out _));
        clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
        Assert.Null(limiter.TryReserve("s", 100, out _));
    }

    [Fact]
    public void Refused_reservation_spends_nothing_and_release_gives_it_back()
    {
        var limiter = new X402SpendLimiter(new X402SpendLimits { PerDay = 100 });

        Assert.NotNull(limiter.TryReserve("s", 101, out _));
        Assert.Equal(0, limiter.SpentToday);

        Assert.Null(limiter.TryReserve("s", 60, out var reservation));
        limiter.Release(reservation);
        Assert.Equal(0, limiter.SpentToday);
        Assert.Null(limiter.TryReserve("s", 100, out _));
    }
}
