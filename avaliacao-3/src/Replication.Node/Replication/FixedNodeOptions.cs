namespace Replication.Node.Replication;

public sealed record FixedNodeOptions
{
    public string NodeId { get; init; } = "node-1";
    public bool IsLeader { get; init; } = true;
    public string? LeaderAddress { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(NodeId);
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
