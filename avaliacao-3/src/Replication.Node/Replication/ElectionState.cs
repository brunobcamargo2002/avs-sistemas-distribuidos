using Replication.Node.Storage;

namespace Replication.Node.Replication;

public sealed class ElectionState
{
    public object SyncRoot { get; } = new();
    private readonly JsonKeyValueStore store;
    private readonly FixedNodeOptions options;
    private readonly ILogger<ElectionState> logger;
    private readonly TimeProvider clock;
    private long deadline;

    public long Term { get; private set; }
    public string Role { get; private set; }
    public string? LeaderId { get; private set; }
    public bool IsLeader => Role is "lider" or "lider fixo";
    public string? LeaderAddress => options.AutomaticElection
        ? LeaderId is not null ? options.Peers[LeaderId] : null
        : options.LeaderAddress;

    public ElectionState(JsonKeyValueStore store, FixedNodeOptions options, ILogger<ElectionState> logger,
        TimeProvider? clock = null)
    {
        this.store = store;
        this.options = options;
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        Term = store.GetElectionState().Term;
        Role = options.AutomaticElection ? "seguidor" : options.IsLeader ? "lider fixo" : "seguidor";
        LeaderId = IsLeader ? options.NodeId : null;
        if (options.AutomaticElection)
        {
            store.EnsureLogBackedState();
            var lastTerm = store.GetLog().LastOrDefault()?.Term ?? 0;
            if (lastTerm > Term)
            {
                store.SaveElectionState(lastTerm, null);
                Term = lastTerm;
            }
        }

        ResetDeadline();
    }

    public bool TimedOut => clock.GetTimestamp() >= deadline;

    public void ObserveTerm(long term)
    {
        if (term <= Term)
        {
            return;
        }

        store.SaveElectionState(term, null);
        Term = term;
        Role = "seguidor";
        LeaderId = null;
        ResetDeadline();
        LogTransition();
    }

    public long StartElection()
    {
        var term = checked(Term + 1);
        store.SaveElectionState(term, options.NodeId);
        Term = term;
        Role = "candidato";
        LeaderId = null;
        ResetDeadline();
        LogTransition();
        return Term;
    }

    public bool Vote(long term, string candidate, long lastIndex, long lastTerm)
    {
        ObserveTerm(term);
        var ownLog = store.GetLogSnapshot();
        var ownTerm = ownLog.Entries.LastOrDefault()?.Term ?? 0;
        var upToDate = lastTerm > ownTerm || lastTerm == ownTerm && lastIndex >= ownLog.Status.LastLogIndex;
        var vote = store.GetElectionState().VotedFor;
        if (term != Term || !upToDate || vote is not null && vote != candidate)
        {
            return false;
        }

        store.SaveElectionState(Term, candidate);
        ResetDeadline();
        return true;
    }

    public void BecomeLeader(long electionTerm)
    {
        if (Role != "candidato" || Term != electionTerm)
        {
            return;
        }

        Role = "lider";
        LeaderId = options.NodeId;
        LogTransition();
    }

    public bool AcceptLeader(long term, string id)
    {
        ObserveTerm(term);
        if (term != Term || !options.Members.Contains(id, StringComparer.Ordinal) || id == options.NodeId)
        {
            return false;
        }

        var changed = Role != "seguidor" || LeaderId != id;
        Role = "seguidor";
        LeaderId = id;
        ResetDeadline();
        if (changed)
        {
            LogTransition();
        }

        return true;
    }

    public void SetLeaderHint(string? id)
    {
        if (id is not null && (!options.Members.Contains(id, StringComparer.Ordinal) || id == options.NodeId))
        {
            throw new ArgumentException("Lider desconhecido.");
        }

        LeaderId = id;
    }

    private void ResetDeadline() =>
        deadline = clock.GetTimestamp()
            + (long)(Random.Shared.Next(1200, 2001) / 1000d * clock.TimestampFrequency);

    private void LogTransition() => logger.LogInformation(
        "No {Node}: papel={Role}; termo={Term}; lider={Leader}; commit={Commit}.",
        options.NodeId, Role, Term, LeaderId ?? "desconhecido", store.GetStatus().CommitIndex);
}
