# FBMA Sales Management

.NET 8 Azure App Service application for recording paid FBMA mobile orders, issuing sequential English PDF receipts, emailing the customer and internal recipients, and tracking operating costs in an on-premises Microsoft SQL Server database running on Linux.

## Numbering and tax

- `OrderId` is the immutable reference supplied by FBMA Mobile.
- Receipt invoice numbers are generated independently as `01`, `02`, `03`, etc.
- VAT is calculated at 5%. The document is intentionally described as a payment receipt and does not contain a TRN.
- Reposting the same `OrderId` is safe and returns the original record without issuing another invoice.

## Configure

Do not put production secrets in `appsettings.json`. Set them through environment variables or an on-premises secret store:

```text
Security__ApiKey
Security__AdminUsername
Security__AdminPassword
ConnectionStrings__SalesDatabase
Smtp__Host
Smtp__Port
Smtp__Username
Smtp__Password
Smtp__SenderEmail
Organization__Name
Organization__InternalEmails__0
Organization__InternalEmails__1
```

For the current Microsoft 365 mailbox, the host is `smtp.office365.com`, the port is `587`, TLS is enabled, and both the username and sender are `developer@fbma.org.ae`. Supply only `Smtp__Password` from the protected on-premises server environment. Do not commit it to this project.

The supplied FBMA logo is stored at `assets/fbma-logo.jpg` and embedded into generated receipts. The organization name and both internal recipients are already configured.

## Database: SQL Server on Linux

Install SQL Server on the on-premises Linux host, then run `deployment/sql-server/bootstrap.sql` with an administrator account. Replace the login password before execution. The application creates its tables on the first successful connection.

The production connection string must be stored in Azure App Service configuration, not in source control:

```text
Server=tcp:fbma-sql-onprem,1433;Database=FbmaSales;User ID=fbma_sales_app;Password=SECRET;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30
```

Use a stable DNS hostname that matches the SQL Server TLS certificate. `TrustServerCertificate=False` must remain enabled for production certificate validation.

## Azure-to-on-premises connection

For one SQL endpoint, configure an Azure App Service Hybrid Connection targeting the on-premises SQL DNS hostname and TCP port `1433`. Install Hybrid Connection Manager on a Linux machine that can reach SQL Server. The manager makes an outbound TLS connection to Azure over port `443`; do not expose SQL port `1433` publicly.

The hostname in the application connection string must exactly match the Hybrid Connection endpoint hostname.

## Deploy Azure resources

The Bicep template in `deployment/azure/main.bicep` creates a Linux .NET 8 App Service plan and application with HTTPS, health checks, and secure settings parameters. Deploy it from Azure CLI, supplying secrets interactively or through an approved secret pipeline:

```bash
az deployment group create \
  --resource-group YOUR_RESOURCE_GROUP \
  --template-file deployment/azure/main.bicep \
  --parameters appName=YOUR_UNIQUE_APP_NAME \
  --parameters sqlConnectionString='YOUR_CONNECTION_STRING' \
  --parameters apiKey='YOUR_RANDOM_API_KEY' \
  --parameters adminPassword='YOUR_RANDOM_ADMIN_PASSWORD' \
  --parameters smtpPassword='YOUR_SMTP_PASSWORD'

dotnet publish -c Release -o publish
cd publish && zip -r ../fbma-sales-management.zip . && cd ..
az webapp deploy \
  --resource-group YOUR_RESOURCE_GROUP \
  --name YOUR_UNIQUE_APP_NAME \
  --src-path fbma-sales-management.zip \
  --type zip
```

Configure the Hybrid Connection in the Azure portal after creating the app: **App Service > Networking > Hybrid connections**.

## Run locally against SQL Server

```bash
dotnet restore
dotnet run --urls http://0.0.0.0:5080
```

Open `http://server-address:5080/` and sign in with the configured admin credentials.

## Record a paid sale

Only the server that has verified the payment should call this endpoint:

```bash
curl -X POST http://server-address:5080/api/sales \
  -H 'Content-Type: application/json' \
  -H 'X-API-Key: your-secret-key' \
  -d '{
    "orderId":"FBMA-ABC1234",
    "customerName":"Test Customer",
    "customerEmail":"customer@example.com",
    "customerPhone":"0500000000",
    "itemDescription":"Bowling - Lane 5",
    "quantity":1,
    "unitPrice":40,
    "paymentDate":"2026-10-19T12:00:00+04:00",
    "paymentMethod":"Credit Card",
    "paymentReference":"MPGS-REFERENCE",
    "bookingDate":"2026-10-20T16:00:00+04:00"
  }'
```

## Record a cost

`POST /api/costs` accepts `description`, `category`, `amount`, `costDate`, and optional `notes`. Use either Basic admin authentication or the API key.

## Production notes

- Keep the Azure App Service HTTPS-only setting enabled.
- Restrict dashboard access to authorized FBMA users and restrict API access where possible.
- Back up the on-premises SQL Server database daily and test restoration regularly.
- Monitor the Linux Hybrid Connection Manager and SQL Server services.
- Replace all placeholder credentials before first use.
- The existing FBMA backend still needs one authenticated call added after verified payment/booking completion.
