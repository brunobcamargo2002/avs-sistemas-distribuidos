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
    private List<StoredLogEntry> log;
    private long commitIndex;
    private long lastApplied;
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

                if (state.FormatVersion is < 1 or > CurrentFormatVersion)
                {
                    throw new JsonException($"Versao de estado nao suportada: {state.FormatVersion}.");
                }

                var persistedLog = state.Log;
                if (state.FormatVersion == LegacyFormatVersion)
                {
                    if (state.CommitIndex != 0 || state.LastApplied != 0
                        || persistedLog is { Count: > 0 })
                    {
                        throw new JsonException("O formato legado nao pode conter log ou indices de commit.");
                    }

                    persistedLog ??= [];
                }
                else
                {
                    if (persistedLog is null)
                    {
                        throw new JsonException("O arquivo nao contem o log de operacoes.");
                    }
                }

                var persistedData = state.Data
                    ?? throw new JsonException("O arquivo nao contem o estado dos dados comitados.");
                ValidateState(state, persistedData, persistedLog);
                data = new Dictionary<string, string>(persistedData, StringComparer.Ordinal);
                log = persistedLog;
                commitIndex = state.CommitIndex;
                lastApplied = state.LastApplied;
            }
            else
            {
                data = new Dictionary<string, string>(StringComparer.Ordinal);
                log = [];
            }
        }
        catch
        {
            ownership.Dispose();
            throw;
        }
    }

    public long Put(string key, string value, long term = 0)
    {
        var index = AppendUncommitted(key, value, term);
        CommitThrough(index);
        return index;
    }

    public long AppendUncommitted(string key, string value, long term = 0)
    {
        ValidateEntry(key, value, term);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var entry = new StoredLogEntry
            {
                Index = log.Count + 1L,
                Term = term,
                Key = key,
                Value = value
            };
            var updatedLog = new List<StoredLogEntry>(log) { entry };
            Persist(updatedLog, data, commitIndex, lastApplied);
            log = updatedLog;
            return entry.Index;
        }
    }

    public void CommitThrough(long index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (index < 0 || index > log.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    "O commit deve avancar sem ultrapassar o ultimo indice do log.");
            }

            if (index <= commitIndex)
            {
                return;
            }

            var updatedData = new Dictionary<string, string>(data, StringComparer.Ordinal);
            foreach (var entry in log.Where(entry => entry.Index > lastApplied && entry.Index <= index))
            {
                updatedData[entry.Key] = entry.Value;
            }

            Persist(log, updatedData, index, index);
            data = updatedData;
            commitIndex = index;
            lastApplied = index;
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

    public IReadOnlyList<LogEntrySnapshot> GetLog()
    {
        return GetLogSnapshot().Entries;
    }

    public LogSnapshot GetLogSnapshot()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new LogSnapshot(
                new StoreStatus(log.Count, commitIndex, lastApplied),
                log.Select(entry => new LogEntrySnapshot(
                    entry.Index, entry.Term, entry.Key, entry.Value, entry.Index <= commitIndex)).ToArray());
        }
    }

    public StoreStatus GetStatus()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new StoreStatus(log.Count, commitIndex, lastApplied);
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

    private static void ValidateEntry(string key, string value, long term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(term);
    }

    private static void ValidateState(
        StoredState state,
        IReadOnlyDictionary<string, string> persistedData,
        IReadOnlyList<StoredLogEntry> persistedLog)
    {
        if (persistedData.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
            || entry.Value is null))
        {
            throw new JsonException("O arquivo contem chaves ou valores invalidos.");
        }

        if (state.CommitIndex < 0 || state.LastApplied != state.CommitIndex
            || state.CommitIndex > persistedLog.Count)
        {
            throw new JsonException("Os indices de commit e aplicacao do estado sao invalidos.");
        }

        for (var index = 0; index < persistedLog.Count; index++)
        {
            var entry = persistedLog[index];
            if (entry is null || entry.Index != index + 1L || entry.Term < 0
                || string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null)
            {
                throw new JsonException("O arquivo contem uma entrada de log invalida.");
            }
        }
    }

    private void Persist(
        List<StoredLogEntry> updatedLog,
        Dictionary<string, string> updatedData,
        long updatedCommitIndex,
        long updatedLastApplied)
    {
        var temporaryPath = FilePath + ".tmp";
        using (var stream = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new StoredState
            {
                FormatVersion = CurrentFormatVersion,
                CommitIndex = updatedCommitIndex,
                LastApplied = updatedLastApplied,
                Log = updatedLog,
                Data = updatedData
            }, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private const int CurrentFormatVersion = 2;
    private const int LegacyFormatVersion = 1;

    public sealed record LogEntrySnapshot(
        long Index, long Term, string Key, string Value, bool IsCommitted);

    public sealed record StoreStatus(long LastLogIndex, long CommitIndex, long LastApplied);

    public sealed record LogSnapshot(StoreStatus Status, IReadOnlyList<LogEntrySnapshot> Entries);

    private sealed class StoredState
    {
        public int FormatVersion { get; init; } = LegacyFormatVersion;
        public long CommitIndex { get; init; }
        public long LastApplied { get; init; }
        public List<StoredLogEntry>? Log { get; init; } = [];
        public Dictionary<string, string>? Data { get; init; }
    }

    private sealed class StoredLogEntry
    {
        public long Index { get; init; }
        public long Term { get; init; }
        public required string Key { get; init; }
        public required string Value { get; init; }
    }
}
