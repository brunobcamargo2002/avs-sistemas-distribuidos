namespace Replication.Node.Replication;

public sealed record FixedNodeOptions
{
    public string NodeId { get; init; } = "node-1";
    public bool IsLeader { get; init; } = true;
    public string? LeaderAddress { get; init; }
    public IReadOnlyList<string> Members { get; init; } = ["node-1", "node-2", "node-3", "node-4"];
    public IReadOnlyDictionary<string, string> Peers { get; init; } = new Dictionary<string, string>();
    public bool AutomaticElection => Peers.Count != 0;

    public const int Quorum = 3;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(NodeId);
        if (Members.Count != 4 || Members.Any(string.IsNullOrWhiteSpace)
            || Members.Distinct(StringComparer.Ordinal).Count() != 4
            || !Members.Contains(NodeId, StringComparer.Ordinal))
        {
            throw new ArgumentException("Configure quatro membros distintos incluindo o proprio --node-id.");
        }
        if (AutomaticElection)
        {
            if (Peers.Count != 4 || Members.Any(member => !Peers.ContainsKey(member))
                || Peers.Values.Any(value => !IsLocalAddress(value))
                || Peers.Values.Select(value => new Uri(value).Port).Distinct().Count() != 4
                || LeaderAddress is not null)
            {
                throw new ArgumentException("--peers deve mapear os quatro membros para portas locais distintas; nao use --leader.");
            }

            return;
        }
        if (IsLeader && LeaderAddress is not null)
        {
            throw new ArgumentException("Um lider nao deve receber --leader.");
        }

        if (!IsLeader && (!Uri.TryCreate(LeaderAddress, UriKind.Absolute, out var address)
            || address.Scheme != "http" || address.Host != "127.0.0.1"
            || address.AbsolutePath != "/" || address.Query != ""
            || address.Fragment != "" || address.UserInfo != ""))
        {
            throw new ArgumentException("O seguidor requer --leader http://127.0.0.1:porta.");
        }
    }

    private static bool IsLocalAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address)
        && address.Scheme == "http" && address.Host == "127.0.0.1"
        && address.Port is >= 1 and <= 65535
        && address.AbsolutePath == "/" && address.Query == ""
        && address.Fragment == "" && address.UserInfo == "";
}
