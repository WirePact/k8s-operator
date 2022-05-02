using KubeOps.Operator;
using Operator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKubernetesOperator(
        settings =>
        {
            settings.Name = "wirepact-operator";
#if DEBUG
            settings.EnableLeaderElection = false;
#endif
        })
// #if DEBUG
//     .AddWebhookLocaltunnel()
// #endif
    ;

builder.Services.AddHostedService<WellKnownTranslators>();
builder.Services.AddHostedService<Pki>();

var app = builder.Build();
app.UseKubernetesOperator();
await app.RunOperatorAsync(args);
