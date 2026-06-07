// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Spike3.Bootstrap;
using Spike3.Contracts;

// Activated by ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Spike3.Bootstrap. ASP.NET reads this attribute when
// it loads the assembly by name and runs the IHostingStartup.
[assembly: HostingStartup(typeof(InspectorHostingStartup))]

namespace Spike3.Bootstrap;

/// <summary>Registers the IStartupFilter that captures the root provider. The clean, no-reflection path.</summary>
public sealed class InspectorHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        Console.WriteLine("[hosting-startup] InspectorHostingStartup.Configure running");
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ProviderCapture>());
    }
}

/// <summary>Captures IApplicationBuilder.ApplicationServices (the app's root IServiceProvider) into
/// InspectorRoots, then lets the rest of the pipeline run unchanged.</summary>
public sealed class ProviderCapture : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        InspectorRoots.Services = app.ApplicationServices;
        Console.WriteLine("[hosting-startup] captured root IServiceProvider via IStartupFilter");
        next(app);
    };
}
