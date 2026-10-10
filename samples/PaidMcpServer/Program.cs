using x402;
using X402.Mcp;

var builder = WebApplication.CreateBuilder(args);

var payTo = builder.Configuration["X402:PayTo"]
    ?? throw new InvalidOperationException("Set X402:PayTo (or env X402__PayTo) to the address that receives payments.");
var facilitatorUrl = builder.Configuration["X402:FacilitatorUrl"] ?? "https://x402.org/facilitator/"; // trailing slash: HttpClient drops the last segment without it

builder.Services.AddHttpClient("nbu", c => c.BaseAddress = new Uri("https://bank.gov.ua/"));
builder.Services.WithHttpFacilitator(facilitatorUrl);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<RateTools>()
    .WithX402Payments(o => o.PayTo = payTo);

var app = builder.Build();
app.MapMcp("/mcp");
app.Run();
