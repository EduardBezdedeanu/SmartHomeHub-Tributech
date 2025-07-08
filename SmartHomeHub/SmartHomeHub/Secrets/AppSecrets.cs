using System.Text.Json;

public class AppSecrets
{
    public Guid ClientId { get; set; } = Guid.Empty;
    public string Host { get; set; } = string.Empty;
    public string HubUser { get; set; } = string.Empty;
    public string HubPassword { get; set; } = string.Empty;
    public string DeviceUser { get; set; } = string.Empty;
    public string DevicePassword { get; set; } = string.Empty;
    public string PfxName { get; set; } = string.Empty;
    public string PfxPassword { get; set; } = string.Empty;
    public string CrtName { get; set; } = string.Empty;
    public static AppSecrets Instance { get; private set; } = new AppSecrets();

    public static void Load(string filePath = "config/secrets.json")
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Configuration file not found: {fullPath}");

        string json = File.ReadAllText(fullPath);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        var deserialized = JsonSerializer.Deserialize<AppSecrets>(json, options);

        if (deserialized == null)
            throw new InvalidOperationException("Error parsing secrets.json");

        // Validation: accept no empty values
        Validate(deserialized);
        Instance = deserialized;
    }

    private static void Validate(AppSecrets secrets)
    {
        var errors = new List<string>();

        if (secrets.ClientId == Guid.Empty)
            errors.Add(nameof(secrets.ClientId));
        if (string.IsNullOrWhiteSpace(secrets.Host))
            errors.Add(nameof(secrets.Host));
        if (string.IsNullOrWhiteSpace(secrets.HubUser))
            errors.Add(nameof(secrets.HubUser));
        if (string.IsNullOrWhiteSpace(secrets.HubPassword))
            errors.Add(nameof(secrets.HubPassword));
        if (string.IsNullOrWhiteSpace(secrets.DeviceUser))
            errors.Add(nameof(secrets.DeviceUser));
        if (string.IsNullOrWhiteSpace(secrets.DevicePassword))
            errors.Add(nameof(secrets.DevicePassword));
       
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Missing configuration values: {string.Join(", ", errors)}");
        }
    }
}
