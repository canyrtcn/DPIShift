using System.Text.Json;
using System.Text.Json.Serialization;
using DPIShift.Core.Models;

namespace DPIShift.Core.Persistence;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _sync = new();

    public string BaseDirectory { get; }
    public string ConfigFilePath { get; }
    public string BackupsDirectory { get; }

    public ConfigStore(string? customBaseDirectory = null)
    {
        BaseDirectory = string.IsNullOrWhiteSpace(customBaseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPIShift")
            : customBaseDirectory;

        ConfigFilePath = Path.Combine(BaseDirectory, "config.json");
        BackupsDirectory = Path.Combine(BaseDirectory, "backups");
    }

    public DpiShiftConfiguration Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return new DpiShiftConfiguration();

                string json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<DpiShiftConfiguration>(json, JsonOptions)
                       ?? new DpiShiftConfiguration();
            }
            catch
            {
                return new DpiShiftConfiguration();
            }
        }
    }

    public void Save(DpiShiftConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            Directory.CreateDirectory(BaseDirectory);
            string tempPath = ConfigFilePath + ".tmp";
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ConfigFilePath, overwrite: true);
        }
    }
}
