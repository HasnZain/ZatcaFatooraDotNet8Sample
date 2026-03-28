using System.Text.Json;
using Microsoft.Extensions.Options;
using ZatcaFatooraDotNet8Sample.Models.Configuration;
using ZatcaFatooraDotNet8Sample.Models.Domain;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class DeviceStateStoreService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public DeviceStateStoreService(IOptions<ZatcaSettings> settings, IWebHostEnvironment environment)
    {
        _filePath = Path.Combine(environment.ContentRootPath, settings.Value.StateFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public async Task<DeviceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new DeviceState();
        }

        await using var stream = File.OpenRead(_filePath);
        var state = await JsonSerializer.DeserializeAsync<DeviceState>(stream, _jsonOptions, cancellationToken);
        return state ?? new DeviceState();
    }

    public async Task SaveAsync(DeviceState state, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
    }
}
