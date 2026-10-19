using System.Text.Json;

namespace consumer_promotions.Data;

public sealed class PromotionDeliveryRepository
{
    private readonly string path;
    private readonly object sync = new();

    public PromotionDeliveryRepository(string path)
    {
        this.path = Path.GetFullPath(path);
    }

    public bool WasSent(string deliveryKey)
    {
        lock (sync)
        {
            return Read().Contains(deliveryKey);
        }
    }

    public void MarkSent(string deliveryKey)
    {
        lock (sync)
        {
            var sent = Read();
            if (!sent.Add(deliveryKey))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sent));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private HashSet<string> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid promotion delivery history.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid promotion delivery history.", exception);
        }
    }
}