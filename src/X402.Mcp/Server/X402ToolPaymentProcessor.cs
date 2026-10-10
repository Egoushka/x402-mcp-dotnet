using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using x402.Core.Enums;
using x402.Core.Interfaces;
using x402.Core.Models.Facilitator;
using x402.Core.Models.v2;
using x402.Facilitator;

namespace X402.Mcp.Server;

/// <summary>
/// Runs one paid tools/call: demand payment, check the payload, verify it with the facilitator,
/// run the tool, settle, and attach the settlement to the result.
/// </summary>
internal sealed class X402ToolPaymentProcessor(
    IFacilitatorV2Client facilitator,
    IAssetInfoProvider assetInfoProvider,
    IOptions<X402McpOptions> options,
    ILogger<X402ToolPaymentProcessor> logger)
{
    private readonly X402McpOptions _options = options.Value;

    public async ValueTask<CallToolResult> HandleAsync(
        RequestContext<CallToolRequestParams> request,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        var tool = request.MatchedPrimitive as McpServerTool;
        var price = tool?.Metadata.OfType<PaidToolAttribute>().LastOrDefault();
        if (tool is null || price is null)
        {
            return await next(request, cancellationToken);
        }

        var toolName = request.Params!.Name;
        var resource = new ResourceInfo
        {
            Url = X402McpProtocol.ToolResourcePrefix + toolName,
            Description = price.Description ?? tool.ProtocolTool.Description ?? toolName,
            MimeType = "application/json",
        };
        var requirement = BuildRequirement(price, toolName);

        if (request.Params.Meta?[X402McpProtocol.PaymentMetaKey] is not { } paymentNode)
        {
            return PaymentRequired(resource, requirement, "Payment required to access this resource");
        }

        PaymentPayloadHeader? payload;
        try
        {
            payload = paymentNode.Deserialize<PaymentPayloadHeader>(X402McpProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogInformation(ex, "Malformed x402 payment for tool {Tool}", toolName);
            payload = null;
        }

        if (payload is null)
        {
            return PaymentRequired(resource, requirement, "Malformed x402 payment payload");
        }

        // The spec carries the resource at the top level; the x402 package carries it in payload.resource.
        payload.Payload.Resource ??= paymentNode["resource"]?["url"]?.GetValue<string>();

        if (Validate(payload, resource, requirement) is { } validationError)
        {
            logger.LogInformation("Rejected x402 payment for tool {Tool}: {Reason}", toolName, validationError);
            return PaymentRequired(resource, requirement, validationError);
        }

        var verification = await facilitator.VerifyAsync(payload, requirement, cancellationToken);
        if (!verification.IsValid)
        {
            logger.LogInformation("Facilitator rejected x402 payment for tool {Tool}: {Reason}", toolName, verification.InvalidReason);
            return PaymentRequired(resource, requirement, verification.InvalidReason ?? "Payment verification failed");
        }

        var result = await next(request, cancellationToken);

        // A failed tool call is not charged: the signed authorization is simply never settled.
        if (result.IsError == true)
        {
            return result;
        }

        SettlementResponse? settlement;
        try
        {
            settlement = await facilitator.SettleAsync(payload, requirement, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "x402 settlement threw for tool {Tool}", toolName);
            settlement = null;
        }

        // The tool already ran, but its output is only released once the payment has settled.
        if (settlement is not { Success: true })
        {
            logger.LogWarning("x402 settlement failed for tool {Tool}: {Reason}", toolName, settlement?.ErrorReason);
            return PaymentRequired(resource, requirement, "Settlement failed" + (settlement?.ErrorReason is { } reason ? $": {reason}" : ""));
        }

        logger.LogInformation("x402 payment settled for tool {Tool}: {Transaction} from {Payer}", toolName, settlement.Transaction, settlement.Payer);
        result.SetPaymentSettlement(settlement);
        return result;
    }

    private PaymentRequirements BuildRequirement(PaidToolAttribute price, string toolName)
    {
        var asset = price.Asset ?? _options.Asset;
        var payTo = price.PayTo ?? _options.PayTo
            ?? throw new InvalidOperationException(
                $"Paid tool '{toolName}' has no PayTo address: set X402McpOptions.PayTo or PaidToolAttribute.PayTo.");
        var assetInfo = assetInfoProvider.GetAssetInfo(asset);

        return new PaymentRequirements
        {
            Scheme = PaymentScheme.Exact,
            Network = price.Network ?? _options.Network ?? assetInfo?.Network
                ?? throw new InvalidOperationException(
                    $"Paid tool '{toolName}' has no network: asset {asset} is unknown, so set a Network explicitly."),
            Amount = price.Amount,
            Asset = asset,
            PayTo = payTo,
            MaxTimeoutSeconds = _options.MaxTimeoutSeconds,
            Extra = assetInfo is null ? null : new PaymentRequirementsExtra { Name = assetInfo.Name, Version = assetInfo.Version },
        };
    }

    // Mirrors the checks x402's HTTP handler runs before calling the facilitator.
    private static string? Validate(PaymentPayloadHeader payload, ResourceInfo resource, PaymentRequirements requirement)
    {
        if (payload.X402Version != X402McpProtocol.Version)
        {
            return $"Unsupported x402 version {payload.X402Version}";
        }

        if (!string.Equals(payload.Payload.Resource?.Trim().TrimEnd('/'), resource.Url, StringComparison.OrdinalIgnoreCase))
        {
            return "Payment payload resource does not match the requested tool";
        }

        var accepted = payload.Accepted;
        var authorization = payload.Payload.Authorization;
        var matches = accepted.Scheme == requirement.Scheme
            && accepted.Network.Equals(requirement.Network, StringComparison.OrdinalIgnoreCase)
            && accepted.Amount == requirement.Amount
            && accepted.Asset.Equals(requirement.Asset, StringComparison.OrdinalIgnoreCase)
            && accepted.PayTo.Equals(requirement.PayTo, StringComparison.OrdinalIgnoreCase)
            && authorization.To.Equals(requirement.PayTo, StringComparison.OrdinalIgnoreCase)
            && BigInteger.TryParse(authorization.Value, out var value)
            && value == BigInteger.Parse(requirement.Amount);
        if (!matches)
        {
            return "No matching payment requirements found for the provided payload";
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!long.TryParse(authorization.ValidBefore, out var validBefore) || validBefore < now)
        {
            return $"Authorization expired: validBefore {authorization.ValidBefore} is in the past";
        }

        if (!long.TryParse(authorization.ValidAfter, out var validAfter) || validAfter > now)
        {
            return $"Authorization not yet valid: validAfter {authorization.ValidAfter} is in the future";
        }

        return null;
    }

    private static CallToolResult PaymentRequired(ResourceInfo resource, PaymentRequirements requirement, string error) =>
        CallToolResultExtensions.CreatePaymentRequired(new PaymentRequiredResponse
        {
            X402Version = X402McpProtocol.Version,
            Error = error,
            Resource = resource,
            Accepts = [requirement],
        });
}
