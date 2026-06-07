// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using Spike4.Contracts;

namespace Spike4.Bootstrap;

/// <summary>
/// Captures the Generic Host's root IServiceProvider with NO source modification and NO ASP.NET, by
/// snooping the "Microsoft.Extensions.Hosting" DiagnosticListener. HostBuilder/HostApplicationBuilder
/// write a "HostBuilt" event whose payload is the IHost; we reflect IHost.Services off it. (This is the
/// same listener HostFactoryResolver / the EF Core tools rely on.) We never reference the Hosting types
/// directly — reflection keeps the bootstrap dependency-free and version-robust.
/// </summary>
internal static class HostCapture
{
    private static IDisposable? allListenersSub;

    public static void Install()
    {
        allListenersSub = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());
        Console.WriteLine("[capture] subscribed to DiagnosticListener.AllListeners (watching for the host)");
    }

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener dl)
        {
            if (dl.Name != "Microsoft.Extensions.Hosting") return;
            Console.WriteLine("[capture] found DiagnosticListener 'Microsoft.Extensions.Hosting'");
            dl.Subscribe(new EventObserver(), (Func<string, object?, object?, bool>)((_, _, _) => true));
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> evt)
        {
            string payloadType = evt.Value?.GetType().FullName ?? "null";
            Console.WriteLine($"[capture] hosting event '{evt.Key}' (payload: {payloadType})");

            // HostBuilt -> payload is IHost (has .Services). HostBuilding -> IHostBuilder (no .Services).
            var services = evt.Value?.GetType().GetProperty("Services")?.GetValue(evt.Value) as IServiceProvider;
            if (services is not null && InspectorRoots.Services is null)
            {
                InspectorRoots.Services = services;
                Console.WriteLine($"[capture] captured root IServiceProvider from the '{evt.Key}' event");
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
