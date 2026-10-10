using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using x402.Facilitator;

namespace X402.Mcp.Tests;

/// <summary>An MCP server with paid tools and a client, connected in process through pipes.</summary>
public sealed class McpServerFixture : IAsyncDisposable
{
    public const string PayTo = "0x209693Bc6afc0C5328bA36FaF03C514EF312287C";

    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly ServiceProvider _services;
    private readonly Task _serverTask;

    public FakeFacilitator Facilitator { get; } = new();
    public ToolCalls ToolCalls { get; } = new();

    public McpServerFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFacilitatorV2Client>(Facilitator);
        services.AddSingleton(ToolCalls);
        services.AddMcpServer()
            .WithStreamServerTransport(_clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream())
            .WithTools<TestTools>()
            .WithX402Payments(o => o.PayTo = PayTo);

        _services = services.BuildServiceProvider(validateScopes: true);
        _serverTask = _services.GetRequiredService<McpServer>().RunAsync(_cts.Token);
    }

    public Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(
            new StreamClientTransport(_clientToServer.Writer.AsStream(), _serverToClient.Reader.AsStream()),
            new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) },
            cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();
        await _serverTask;
        await _services.DisposeAsync();
        _cts.Dispose();
    }
}
