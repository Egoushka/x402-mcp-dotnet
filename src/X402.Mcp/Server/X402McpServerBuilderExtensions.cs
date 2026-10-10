using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using x402.Core;
using x402.Core.Interfaces;
using X402.Mcp;
using X402.Mcp.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers x402 payments for MCP tools marked with <see cref="PaidToolAttribute"/>.</summary>
public static class X402McpServerBuilderExtensions
{
    /// <summary>
    /// Charges for every tool marked with <see cref="PaidToolAttribute"/>. Needs an
    /// <c>IFacilitatorV2Client</c>, e.g. from <c>services.WithHttpFacilitator(url)</c> of the x402 package.
    /// </summary>
    public static IMcpServerBuilder WithX402Payments(this IMcpServerBuilder builder, Action<X402McpOptions>? configure = null)
    {
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.AddOptions<X402McpOptions>();
        }

        builder.Services.TryAddSingleton<IAssetInfoProvider, AssetInfoProvider>();
        builder.Services.TryAddTransient<X402ToolPaymentProcessor>();

        return builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => (request, cancellationToken) =>
        {
            var services = request.Services ?? throw new InvalidOperationException("MCP request has no service provider.");
            return services.GetRequiredService<X402ToolPaymentProcessor>().HandleAsync(request, next, cancellationToken);
        }));
    }
}
