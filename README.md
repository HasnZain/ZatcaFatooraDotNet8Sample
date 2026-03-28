# ZATCA Fatoora .NET 8 API Sample

This is a simple ASP.NET Core 8 Web API sample that implements the main **ZATCA Fatoora** workflow **without using the ZATCA SDK**.

## What is included

- Phase 1 support
  - UBL invoice XML generation
  - QR TLV generation for simplified invoices
- Phase 2 support
  - Device key pair generation
  - CSR generation
  - Compliance CSID onboarding
  - Compliance invoice submission
  - Production CSID onboarding
  - Standard invoice clearance endpoint wrapper
  - Simplified invoice reporting endpoint wrapper
- Swagger UI for testing
- Simple file-based state store so you can test the flow quickly

## Project structure

- `Controllers/`
  - `CsrController.cs`
  - `OnboardingController.cs`
  - `InvoicesController.cs`
- `Services/`
  - `CsrGeneratorService.cs`
  - `ZatcaApiClient.cs`
  - `InvoiceXmlBuilderService.cs`
  - `InvoiceSignerService.cs`
  - `InvoiceQrService.cs`
  - `DeviceStateStoreService.cs`
- `Models/`
  - request/response/config/domain models
- `App_Data/device-state.json`
  - generated automatically after the first request

## Important note before testing

This project is designed as a **clean starter/integration sample** so you can move the logic into your Vendix project.

Because I cannot access the live ZATCA documentation from this environment, this sample is based on the known endpoint flow and structure up to August 2025. You should verify these before production use:

- exact base URL for your target environment
- exact endpoint paths for sandbox/simulation/production
- exact CSR subject and extension requirements required for your taxpayer/device type
- exact UBL profile and signature requirements expected by your environment

The project already keeps all endpoint paths configurable in `appsettings.json`.

## appsettings.json

Default settings:

```json
"Zatca": {
  "BaseUrl": "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal",
  "ComplianceCsidEndpoint": "/compliance",
  "ComplianceChecksEndpoint": "/compliance/invoices",
  "ProductionCsidEndpoint": "/production/csids",
  "ReportingEndpoint": "/invoices/reporting/single",
  "ClearanceEndpoint": "/invoices/clearance/single",
  "AcceptVersion": "V2",
  "StateFilePath": "App_Data/device-state.json",
  "DefaultCurrencyCode": "SAR",
  "DefaultTaxCurrencyCode": "SAR"
}
```

Change `BaseUrl` and endpoint paths if your ZATCA environment requires different values.

## How to run

```bash
dotnet restore
dotnet run
```

Open Swagger:

- `https://localhost:<port>/swagger`

## Testing flow

### 1) Generate CSR

`POST /api/csr/generate`

Example body:

```json
{
  "commonName": "300000000000003",
  "organizationName": "My Company LLC",
  "organizationalUnitName": "Main Branch",
  "countryCode": "SA",
  "emailAddress": "dev@example.com",
  "serialNumber": "1-TST|2-TST|3-1234567890",
  "organizationIdentifier": "311111111100003"
}
```

The response returns:

- private key PEM
- public key PEM
- CSR PEM
- CSR base64

The same values are also saved into `App_Data/device-state.json`.

### 2) Request compliance CSID

`POST /api/onboarding/compliance-csid`

Example body:

```json
{
  "otp": "123456"
}
```

If successful, the project stores:

- compliance request ID
- compliance binary security token
- compliance secret

### 3) Generate an invoice

`POST /api/invoices/generate`

#### Standard invoice example

```json
{
  "invoiceNumber": "INV-1001",
  "invoiceCategory": "Standard",
  "invoiceTypeCode": "388",
  "seller": {
    "registrationName": "My Company LLC",
    "vatNumber": "311111111100003",
    "registrationNumber": "1010101010",
    "streetName": "King Fahd Road",
    "buildingNumber": "1234",
    "cityName": "Riyadh",
    "postalZone": "12345",
    "countryCode": "SA"
  },
  "buyer": {
    "registrationName": "Customer Company",
    "vatNumber": "399999999900003",
    "registrationNumber": "7000000000",
    "streetName": "Olaya Street",
    "cityName": "Riyadh",
    "postalZone": "12211",
    "countryCode": "SA"
  },
  "lines": [
    {
      "name": "Consulting Service",
      "quantity": 1,
      "unitPrice": 100,
      "discountAmount": 0,
      "taxRate": 15,
      "taxCategoryCode": "S",
      "unitCode": "PCE"
    }
  ]
}
```

#### Simplified invoice example

```json
{
  "invoiceNumber": "SIM-1001",
  "invoiceCategory": "Simplified",
  "invoiceTypeCode": "388",
  "seller": {
    "registrationName": "My Company LLC",
    "vatNumber": "311111111100003",
    "registrationNumber": "1010101010",
    "streetName": "King Fahd Road",
    "buildingNumber": "1234",
    "cityName": "Riyadh",
    "postalZone": "12345",
    "countryCode": "SA"
  },
  "lines": [
    {
      "name": "Retail Item",
      "quantity": 2,
      "unitPrice": 50,
      "discountAmount": 0,
      "taxRate": 15,
      "taxCategoryCode": "S",
      "unitCode": "PCE"
    }
  ]
}
```

The response returns:

- `uuid`
- `invoiceCounterValue`
- `invoiceHashBase64`
- `signatureBase64`
- `qrCodeBase64`
- `signedXml`
- `invoiceBase64`

### 4) Compliance check (before production CSID)

`POST /api/onboarding/compliance-check`

Use values returned by `api/invoices/generate`:

```json
{
  "invoiceHash": "...",
  "uuid": "...",
  "invoiceBase64": "..."
}
```

### 5) Request production CSID

`POST /api/onboarding/production-csid`

Example body:

```json
{}
```

It uses the compliance request ID saved earlier unless you send a value explicitly.

### 6) Submit invoices in Phase 2

#### Simplified invoice reporting

`POST /api/invoices/report-simplified`

```json
{
  "invoiceHash": "...",
  "uuid": "...",
  "invoiceBase64": "..."
}
```

#### Standard invoice clearance

`POST /api/invoices/clear-standard`

```json
{
  "invoiceHash": "...",
  "uuid": "...",
  "invoiceBase64": "..."
}
```

## Transfer notes for Vendix

The easiest migration path into your main project is:

1. Move the service classes from `Services/`
2. Move the DTOs from `Models/`
3. Replace `DeviceStateStoreService` with your own database storage
4. Replace the sample invoice request model with your Vendix invoice tables/entities
5. Keep the ZATCA endpoints in configuration only

## Recommended production improvements

Before using this in production, add:

- persistent database storage instead of a json file
- better validation of CSR fields and invoice data
- stronger XML/UBL conformance validation
- richer tax category handling (zero-rated, exempt, out-of-scope)
- stricter signature conformance against your exact ZATCA profile
- retry/logging/monitoring around ZATCA API calls
- per-device state instead of one global sample state file

## Disclaimer

This sample intentionally favors readability and transferability into an existing ERP/POS codebase. It is a strong starting implementation, but production acceptance still depends on your exact taxpayer onboarding data and your target ZATCA environment accepting the generated CSR/XML/signature format.
