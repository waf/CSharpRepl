// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CSharpRepl.InjectedHook.Contracts;

namespace CSharpRepl.InjectedHook;

/// <summary>
/// Captures the target's root IServiceProvider into ConnectorRoots.Services by observing the
/// "Microsoft.Extensions.Hosting" DiagnosticListener.
///
/// - HostBuilder.Build() and HostApplicationBuilder.Build() both write a "HostBuilt" event whose payload is
///   the IHost; its Services property is reflected off it. Covers the non-web Generic Host and modern
///   ASP.NET Core (WebApplication.CreateBuilder builds through HostApplicationBuilder).
/// - Same listener HostFactoryResolver and the EF Core design-time tools rely on — the listener and event
///   names are an effectively public contract.
/// - Install must run before the target's Main so the subscription precedes the host build — the startup
///   hook guarantees this.
/// </summary>
internal static class HostCapture
{
    private const string HostingListenerName = "Microsoft.Extensions.Hosting";

    private static IDisposable? allListenersSubscription;

    public static void Install()
    {
        try
        {
            allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());
        }
        catch
        {
            // No capture means `services`/Get<T>() stay unavailable (statics still work); never fail the hook.
        }
    }

    /// <summary>Called on capture: stop observing entirely — the connector only needs the first root provider.</summary>
    private static void Captured()
    {
        try { allListenersSubscription?.Dispose(); } catch { /* best effort */ }
        allListenersSubscription = null;
    }

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener listener)
        {
            try
            {
                if (listener.Name == HostingListenerName && ConnectorRoots.Services is null)
                {
                    // Subscribing (with no predicate) makes the listener's IsEnabled("HostBuilt") true, which
                    // is what gates the host's write of the event we're waiting for.
                    listener.Subscribe(new EventObserver());
                }
            }
            catch
            {
                // Swallow everything: this runs inline in the target's code that created the listener.
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> hostingEvent)
        {
            try
            {
                // "HostBuilt" carries the IHost (which has .Services — the root provider); "HostBuilding"
                // carries the builder (no Services property) and reflects to null. Reflection, not a cast:
                // the payload's concrete type is internal, and referencing hosting abstractions here would
                // drag a version-specific dependency into the target's default ALC.
                var services = hostingEvent.Value?.GetType().GetProperty("Services")?.GetValue(hostingEvent.Value) as IServiceProvider;
                if (services is not null &&
                    Interlocked.CompareExchange(ref ConnectorRoots.Services, services, null) is null)
                {
                    Captured();
                }
            }
            catch
            {
                // Swallow everything: this runs inline inside the target's Host.Build().
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
