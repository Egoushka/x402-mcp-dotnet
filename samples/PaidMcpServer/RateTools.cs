using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using X402.Mcp;

[McpServerToolType]
public sealed class RateTools
{
    [McpServerTool(Name = "ping"), Description("Free health check.")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "nbu_rate"), Description("Official National Bank of Ukraine exchange rate of a currency to UAH.")]
    [PaidTool("1000")] // 0.001 USDC
    public static async Task<string> NbuRate(
        IHttpClientFactory httpClientFactory,
        [Description("ISO 4217 code, e.g. USD or EUR")] string currency,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("nbu");
        var url = $"NBUStatService/v1/statdirectory/exchange?valcode={Uri.EscapeDataString(currency.ToUpperInvariant())}&json";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken));

        if (doc.RootElement.GetArrayLength() == 0)
        {
            throw new ArgumentException($"NBU publishes no rate for '{currency}'.");
        }

        var rate = doc.RootElement[0];
        return $"1 {rate.GetProperty("cc").GetString()} = {rate.GetProperty("rate").GetDecimal().ToString(CultureInfo.InvariantCulture)} UAH on {rate.GetProperty("exchangedate").GetString()}";
    }
}
