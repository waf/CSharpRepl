// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Target.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // keep stdout focused on the [hook]/[inspector]/[target] lines
builder.Services.AddSingleton<IOrderService, OrderService>();

var app = builder.Build();
app.MapGet("/", () => "ok");
app.MapPost("/order", (IOrderService svc) => { svc.AddPending(); return Results.Ok(svc.PendingCount); });

Console.WriteLine($"[target] ASP.NET app starting (pid {Environment.ProcessId})");
app.Run("http://127.0.0.1:52199");
Console.WriteLine("[target] ASP.NET app stopped cleanly");

namespace Target.Web
{
    /// <summary>A DI singleton holding live state. The inspector reads it through the captured provider.</summary>
    public interface IOrderService
    {
        int PendingCount { get; }
        void AddPending();
    }

    public sealed class OrderService : IOrderService
    {
        private int pending;
        public int PendingCount => pending;
        public void AddPending() => Interlocked.Increment(ref pending);
    }
}
