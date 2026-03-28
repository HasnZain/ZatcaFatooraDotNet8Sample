using ZatcaFatooraDotNet8Sample.Models.Configuration;
using ZatcaFatooraDotNet8Sample.Services;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
