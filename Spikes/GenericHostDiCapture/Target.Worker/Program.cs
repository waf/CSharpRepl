// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerApp;

bool useAppBuilder = args.Contains("--app-builder");
Console.WriteLine($"[target] Generic Host console starting (pid {Environment.ProcessId}), " +
                  $"builder={(useAppBuilder ? "HostApplicationBuilder" : "HostBuilder")}");

IHost host;
if (useAppBuilder)
{
    var b = Host.CreateApplicationBuilder(args);
    b.Logging.ClearProviders();
    b.Services.AddSingleton<ICounter, Counter>();
    b.Services.AddHostedService<Worker>();
    host = b.Build();
}
else
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureLogging(l => l.ClearProviders())
        .ConfigureServices(s =>
        {
            s.AddSingleton<ICounter, Counter>();
            s.AddHostedService<Worker>();
        })
        .Build();
}

host.Run();
Console.WriteLine("[target] Generic Host stopped cleanly");

namespace WorkerApp
{
    public interface ICounter
    {
        int Count { get; }
        int Mark { get; set; }
        void Increment();
    }

    public sealed class Counter : ICounter
    {
        private int count;
        public int Count => count;
        public int Mark { get; set; }
        public void Increment() => Interlocked.Increment(ref count);
    }

    /// <summary>Background service that mutates the DI singleton — the live state the inspector reads.</summary>
    public sealed class Worker : BackgroundService
    {
        private readonly ICounter counter;
        private readonly IHostApplicationLifetime life;

        public Worker(ICounter counter, IHostApplicationLifetime life)
        {
            this.counter = counter;
            this.life = life;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[worker] started; incrementing the DI singleton counter");
            int ticks = 0;
            bool acked = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                counter.Increment();
                ticks++;
                if (!acked && counter.Mark == 9999)
                {
                    acked = true;
                    Console.WriteLine("[worker] observed the inspector's write (Mark == 9999)");
                }
                if (ticks >= 60) // ~12s safety net so the process always exits even if capture fails
                {
                    Console.WriteLine("[worker] safety timeout reached; stopping");
                    life.StopApplication();
                    break;
                }
                try { await Task.Delay(200, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
