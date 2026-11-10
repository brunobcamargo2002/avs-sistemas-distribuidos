namespace Replication.Node.Replication;

public sealed record FixedNodeOptions
{
    public string NodeId { get; init; } = "node-1";
    public bool IsLeader { get; init; } = true;
    public string? LeaderAddress { get; init; }
    public IReadOnlyList<string> Members { get; init; } = ["node-1", "node-2", "node-3", "node-4"];

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
}
