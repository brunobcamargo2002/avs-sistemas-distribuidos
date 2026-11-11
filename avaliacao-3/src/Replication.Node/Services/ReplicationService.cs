using Grpc.Core;
using Replication.Contracts;
using Replication.Node.Replication;
using Replication.Node.Storage;

namespace Replication.Node.Services;

public sealed class ReplicationService(
    JsonKeyValueStore store,
    FixedNodeOptions options,
    ElectionState election,
    QuorumCoordinator quorum,
    ILogger<ReplicationService> logger) : ReplicationTransport.ReplicationTransportBase
{
    public override Task<VoteResponse> RequestVote(VoteRequest request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!options.AutomaticElection || !options.Members.Contains(request.CandidateId, StringComparer.Ordinal)
            || request.CandidateId == options.NodeId || request.Term < 1
            || request.LastLogIndex < 0 || request.LastLogTerm < 0 || request.LastLogTerm > request.Term
            || request.LastLogIndex == 0 && request.LastLogTerm != 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Pedido de voto invalido."));
        }

        lock (election.SyncRoot)
        {
            try
            {
                var granted = election.Vote(request.Term, request.CandidateId, request.LastLogIndex, request.LastLogTerm);
                logger.LogInformation("Voto para {Candidate} no termo {Term}: {Granted}.",
                    request.CandidateId, request.Term, granted);
                return Task.FromResult(new VoteResponse { Term = election.Term, Granted = granted });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Nao foi possivel persistir termo/voto.");
                throw new RpcException(new Status(StatusCode.Internal, "Falha ao persistir termo/voto."));
            }
        }
    }

    public override Task<LeaderResponse> GetLeader(LeaderRequest request, ServerCallContext context)
    {
        lock (election.SyncRoot)
        {
            return Task.FromResult(new LeaderResponse
            {
                Term = election.Term,
                LeaderId = election.LeaderId ?? "",
                Role = election.Role
            });
        }
    }

    public override Task<FetchResponse> Fetch(FetchRequest request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.FollowerId) || request.FollowerId == options.NodeId
            || !options.Members.Contains(request.FollowerId, StringComparer.Ordinal)
            || request.Term < 0 || request.LastLogIndex < 0 || request.LastLogTerm < 0
            || (request.LastLogIndex == 0 && request.LastLogTerm != 0))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Identidade ou posicao do seguidor invalida."));
        }

        lock (election.SyncRoot)
        {
            try
            {
                if (options.AutomaticElection)
                {
                    election.ObserveTerm(request.Term);
                }

                if (!election.IsLeader || options.AutomaticElection && request.Term != election.Term)
                {
                    if (options.AutomaticElection)
                    {
                        return Task.FromResult(new FetchResponse
                        {
                            Term = election.Term,
                            LeaderId = election.LeaderId ?? ""
                        });
                    }

                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Este no nao e o lider fixo."));
                }

                store.EnsureLogBackedState();
                var snapshot = store.GetLogSnapshot();
                if (request.LastLogIndex > snapshot.Status.LastLogIndex
                    || (request.LastLogIndex > 0
                        && snapshot.Entries[(int)request.LastLogIndex - 1].Term != request.LastLogTerm))
                {
                    if (options.AutomaticElection)
                    {
                        return Task.FromResult(new FetchResponse
                        {
                            Term = election.Term,
                            LeaderId = options.NodeId,
                            LogConflict = true
                        });
                    }

                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        "Log divergente. Truncamento ainda nao implementado."));
                }

                quorum.Confirm(request.FollowerId, request.LastLogIndex);
                var response = new FetchResponse
                {
                    LeaderId = options.NodeId,
                    Term = election.Term,
                    Accepted = true,
                    LeaderCommit = store.GetStatus().CommitIndex
                };
                response.Entries.AddRange(snapshot.Entries.Skip((int)request.LastLogIndex).Take(128)
                    .Select(entry => new LogEntry
                    {
                        Index = entry.Index,
                        Term = entry.Term,
                        Key = entry.Key,
                        Value = entry.Value
                    }));
                return Task.FromResult(response);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(exception, "Estado do lider incompativel com replicacao por log.");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Falha ao persistir termo ou commit durante Fetch.");
                throw new RpcException(new Status(StatusCode.Internal, "Nao foi possivel persistir o estado."));
            }
        }
    }
}
