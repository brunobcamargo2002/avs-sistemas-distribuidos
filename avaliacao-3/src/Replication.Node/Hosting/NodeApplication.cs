using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Replication.Node.Services;
using Replication.Node.Storage;

namespace Replication.Node.Hosting;

public static class NodeApplication
{
    public static WebApplication Create(string dataPath, int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, port, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(_ => new JsonKeyValueStore(dataPath));

        var app = builder.Build();
        app.MapGrpcService<StorageService>();
        return app;
    }
}
