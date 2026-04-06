using ZatcaFatooraDotNet8Sample.Models.Configuration;
using ZatcaFatooraDotNet8Sample.Services;
using ZatcaFatooraDotNet8Sample.Utilities;
using System.Security.Cryptography.Xml;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Register ECDSA-SHA256 signature description for ZATCA XML signatures
CryptoConfig.AddAlgorithm(typeof(EcdsaSha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256");

builder.Services.Configure<ZatcaSettings>(builder.Configuration.GetSection(ZatcaSettings.SectionName));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient<ZatcaApiClient>();
builder.Services.AddSingleton<DeviceStateStoreService>();
builder.Services.AddSingleton<CsrGeneratorService>();
builder.Services.AddSingleton<InvoiceQrService>();
builder.Services.AddSingleton<InvoiceSignerService>();
builder.Services.AddSingleton<InvoiceXmlBuilderService>();
builder.Services.AddSingleton<SimplifiedInvoiceSignerService>();
builder.Services.AddSingleton<IInvoiceSignerService, InvoiceSignerService>();
builder.Services.AddSingleton<InvoiceSignerResolverService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
