using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using Operator.Entities;

namespace Operator.Services;

public class Pki : TimedService
{
    private readonly ILogger<Pki> _logger;
    private readonly IKubernetesClient _client;

    public Pki(ILogger<Pki> logger, IKubernetesClient client)
        : base(TimeSpan.FromMinutes(5))
    {
        _logger = logger;
        _client = client;
    }

    protected override async void Execute()
    {
        _logger.LogDebug("Check for existing PKI.");
        var pkis = await _client.List<V1Alpha1Pki>();
        if (pkis.Any())
        {
            return;
        }

        _logger.LogInformation("PKI not found. Create new PKI in namespace.");
        var pki = new V1Alpha1Pki().Initialize();

        pki.Metadata.Name = "wirepact-pki";
        pki.SetLabel("app.kubernetes.io/name", "wirepact-pki");
        pki.SetLabel("app.kubernetes.io/part-of", "wirepact");
        pki.SetLabel("app.kubernetes.io/component", "pki");
        pki.SetLabel("app.kubernetes.io/created-by", "wirepact-operator");

        await _client.Create(pki);
    }
}
