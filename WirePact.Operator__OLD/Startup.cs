using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WirePact.Operator.Controller;

namespace WirePact.Operator;

internal class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddKubernetesOperator(
                s =>
                {
                    s.Name = "wirepact-operator";
#if DEBUG
                    s.EnableLeaderElection = false;
#endif
                })
#if DEBUG
            .AddWebhookLocaltunnel()
#endif
            ;

        services.AddHostedService<PkiController>();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseKubernetesOperator();
    }
}
