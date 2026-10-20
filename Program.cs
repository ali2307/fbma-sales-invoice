using System.Net;
using System.Security.Cryptography;
using System.Text;
using FBMA.SalesManagement;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<OrganizationOptions>(builder.Configuration.GetSection("Organization"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<SalesDatabase>();
builder.Services.AddSingleton<ReceiptPdfService>();
builder.Services.AddSingleton<ReceiptEmailService>();

var app = builder.Build();
await app.Services.GetRequiredService<SalesDatabase>().InitializeAsync();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/sales", async (HttpContext context, SaleRequest request, SalesDatabase database,
    ReceiptPdfService pdfService, ReceiptEmailService emailService, IOptions<SecurityOptions> security,
    IOptions<OrganizationOptions> organization) =>
{
    if (!SecureEquals(context.Request.Headers["X-API-Key"], security.Value.ApiKey)) return Results.Unauthorized();
    var (sale, created) = await database.CreateSaleAsync(request, organization.Value.Currency);
    if (created)
    {
        var emailed = await emailService.SendAsync(sale, pdfService.Create(sale));
        await database.SetEmailStatusAsync(sale.Id, emailed ? "Sent" : "Pending");
        sale = sale with { EmailStatus = emailed ? "Sent" : "Pending" };
    }
    return Results.Ok(new { created, sale.Id, sale.InvoiceNumber, sale.OrderId, sale.Total, sale.Currency, sale.EmailStatus });
});

app.MapGet("/api/sales/{id:long}/receipt", async (HttpContext context, long id, SalesDatabase database,
    ReceiptPdfService pdfService, IOptions<SecurityOptions> security) =>
{
    if (!IsAuthorized(context, security.Value)) return Results.Unauthorized();
    var sale = await database.GetSaleAsync(id);
    return sale is null ? Results.NotFound() : Results.File(pdfService.Create(sale), "application/pdf", $"receipt-{sale.InvoiceNumber}.pdf");
});

app.MapPost("/api/costs", async (HttpContext context, CostRequest request, SalesDatabase database,
    IOptions<SecurityOptions> security) =>
{
    if (!IsAuthorized(context, security.Value)) return Results.Unauthorized();
    return Results.Ok(await database.CreateCostAsync(request));
});

app.MapGet("/", async (HttpContext context, SalesDatabase database, IOptions<SecurityOptions> security,
    IOptions<OrganizationOptions> organization) =>
{
    if (!IsAuthorized(context, security.Value))
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"FBMA Sales Management\"";
        return Results.Unauthorized();
    }

    var sales = await database.GetSalesAsync();
    var costs = await database.GetCostsAsync();
    var revenue = sales.Sum(s => s.Total);
    var expense = costs.Sum(c => c.Amount);
    var rows = string.Join("", sales.Select(s => $"<tr><td>{H(s.InvoiceNumber)}</td><td>{H(s.OrderId)}</td><td>{H(s.CustomerName)}</td><td>{H(s.ItemDescription)}</td><td>{s.PaymentDate.LocalDateTime:dd MMM yyyy}</td><td class='money'>{s.Total:N2} {H(s.Currency)}</td><td><span class='status'>{H(s.EmailStatus)}</span></td><td><a href='/api/sales/{s.Id}/receipt'>PDF</a></td></tr>"));
    var costRows = string.Join("", costs.Select(c => $"<tr><td>{c.CostDate:dd MMM yyyy}</td><td>{H(c.Category)}</td><td>{H(c.Description)}</td><td class='money'>{c.Amount:N2}</td><td>{H(c.Notes)}</td></tr>"));
    return Results.Content(Dashboard(organization.Value.Name, revenue, expense, sales.Count, rows, costRows), "text/html");
});

app.Run();

static bool IsAuthorized(HttpContext context, SecurityOptions security)
{
    if (SecureEquals(context.Request.Headers["X-API-Key"], security.ApiKey)) return true;
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
    try
    {
        var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..])).Split(':', 2);
        return credentials.Length == 2 && SecureEquals(credentials[0], security.AdminUsername) && SecureEquals(credentials[1], security.AdminPassword);
    }
    catch { return false; }
}

static bool SecureEquals(string? supplied, string expected)
{
    if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(expected)) return false;
    var left = Encoding.UTF8.GetBytes(supplied);
    var right = Encoding.UTF8.GetBytes(expected);
    return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}

static string H(string? value) => WebUtility.HtmlEncode(value ?? "");

static string Dashboard(string organization, decimal revenue, decimal costs, int count, string sales, string costRows) => $$$"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{{H(organization)}}} Sales Management</title><style>
:root{--ink:#24191d;--muted:#796b70;--brand:#6c1830;--accent:#ef4b31;--line:#eadfe2;--bg:#f7f3f1}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px Arial,sans-serif}.top{padding:28px 5%;color:white;background:linear-gradient(120deg,var(--brand),var(--accent))}.top h1{margin:0 0 5px;font-size:28px}.wrap{width:min(1180px,92%);margin:24px auto}.cards{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}.card,.panel{background:white;border:1px solid var(--line);border-radius:14px;padding:20px}.label{color:var(--muted);text-transform:uppercase;font-size:11px;letter-spacing:1px}.value{font-size:28px;font-weight:700;margin-top:8px}.panel{margin-top:18px;overflow:auto}.panel h2{margin:0 0 16px}table{width:100%;border-collapse:collapse;white-space:nowrap}th,td{text-align:left;padding:12px;border-bottom:1px solid var(--line)}th{color:var(--muted);font-size:11px;text-transform:uppercase}.money{font-weight:700}.status{padding:5px 9px;border-radius:20px;background:#e7f6ed;color:#17683b}a{color:var(--brand);font-weight:700}@media(max-width:720px){.cards{grid-template-columns:1fr}}
</style></head><body><header class="top"><h1>{{{H(organization)}}}</h1><div>On-premises sales and cost management</div></header><main class="wrap">
<section class="cards"><div class="card"><div class="label">Total collected</div><div class="value">{{{revenue:N2}}} AED</div></div><div class="card"><div class="label">Recorded costs</div><div class="value">{{{costs:N2}}} AED</div></div><div class="card"><div class="label">Net</div><div class="value">{{{revenue-costs:N2}}} AED</div><div>{{{count}}} sales</div></div></section>
<section class="panel"><h2>Sales</h2><table><thead><tr><th>Invoice</th><th>Order ID</th><th>Customer</th><th>Item</th><th>Paid</th><th>Total</th><th>Email</th><th>Receipt</th></tr></thead><tbody>{{{sales}}}</tbody></table></section>
<section class="panel"><h2>Costs</h2><table><thead><tr><th>Date</th><th>Category</th><th>Description</th><th>Amount (AED)</th><th>Notes</th></tr></thead><tbody>{{{costRows}}}</tbody></table></section>
</main></body></html>
""";
