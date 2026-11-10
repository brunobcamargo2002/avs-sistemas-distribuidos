using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Replication.Node.Services;
using Replication.Node.Storage;
using Replication.Node.Replication;

namespace Replication.Node.Hosting;

public static class NodeApplication
{
    public static WebApplication Create(string dataPath, int port, FixedNodeOptions? nodeOptions = null)
    {
        var configuration = nodeOptions ?? new FixedNodeOptions();
        configuration.Validate();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, port, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(_ => new JsonKeyValueStore(dataPath));
        builder.Services.AddSingleton(configuration);
        if (!configuration.IsLeader)
        {
            builder.Services.AddHostedService<PullReplicationWorker>();
        }

        var app = builder.Build();
        app.MapGrpcService<StorageService>();
        app.MapGrpcService<ReplicationService>();
        return app;
    }
}
