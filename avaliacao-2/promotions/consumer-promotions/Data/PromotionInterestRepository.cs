using System.Text.Json;
using consumer_promotions.Models;

namespace consumer_promotions.Data;

public sealed class PromotionInterestRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;
    private readonly object sync = new();

    public PromotionInterestRepository(string path)
    {
        this.path = Path.GetFullPath(path);
    }

    public bool Apply(PromotionInterestEvent interest, bool cancel)
    {
        lock (sync)
        {
            var subscriptions = Read();
            var existing = subscriptions.Find(subscription =>
                string.Equals(subscription.Email, interest.Email, StringComparison.OrdinalIgnoreCase));
            if (existing is not null &&
                (existing.LastEventId == interest.EventId || existing.UpdatedAt >= interest.OccurredAt))
            {
                return false;
            }

            if (existing is not null)
            {
                subscriptions.Remove(existing);
            }

            subscriptions.Add(new PromotionSubscription(interest.Email,
                cancel ? [] : interest.Categories.Distinct(StringComparer.Ordinal).ToArray(),
                interest.OccurredAt, interest.EventId));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(subscriptions, JsonOptions));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return true;
        }
    }

    public IReadOnlyList<string> FindRecipients(string category)
    {
        lock (sync)
        {
            return Read().Where(subscription => subscription.Categories.Any(interest =>
                    category.Equals(interest, StringComparison.Ordinal) ||
                    category.StartsWith(interest + ".", StringComparison.Ordinal)))
                .Select(subscription => subscription.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private List<PromotionSubscription> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PromotionSubscription>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid promotion interests file.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid promotion interests file.", exception);
        }
    }
}

public sealed record PromotionSubscription(string Email, string[] Categories, DateTimeOffset UpdatedAt, Guid LastEventId);