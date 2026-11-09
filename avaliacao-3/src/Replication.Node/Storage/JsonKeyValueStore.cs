using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Replication.Node.Storage;

public sealed class JsonKeyValueStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object gate = new();
    private readonly FileStream ownership;
    private Dictionary<string, string> data;
    private bool disposed;

    public string FilePath { get; }

    public JsonKeyValueStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        ownership = new FileStream(
            FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                var state = JsonSerializer.Deserialize<StoredState>(stream, JsonOptions)
                    ?? throw new JsonException("O arquivo de estado nao pode conter null.");

                if (state.Data is null || state.Data.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
                    || entry.Value is null))
                {
                    throw new JsonException("O arquivo contem chaves ou valores invalidos.");
                }

                data = new Dictionary<string, string>(state.Data, StringComparer.Ordinal);
            }
            else
            {
                data = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
        catch
        {
            ownership.Dispose();
            throw;
        }
    }

    public void Put(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var updated = new Dictionary<string, string>(data, StringComparer.Ordinal)
            {
                [key] = value
            };
            var temporaryPath = FilePath + ".tmp";

            // O arquivo oficial so e substituido depois que a nova versao foi gravada.
            using (var stream = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoredState { Data = updated }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
            data = updated;
        }
    }

    public bool TryGet(string key, [NotNullWhen(true)] out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return data.TryGetValue(key, out value);
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ownership.Dispose();
        }
    }

    private sealed class StoredState
    {
        public required Dictionary<string, string> Data { get; init; }
    }
}
