using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using Operator.Entities;

namespace Operator.Services;

public class WellKnownTranslators : TimedService
{
    private readonly ILogger<WellKnownTranslators> _logger;
    private readonly IKubernetesClient _client;

    private static readonly Dictionary<string, string> Translators = new()
    {
        { "basic-auth", "ghcr.io/wirepact/k8s-basic-auth-translator:latest" },
        { "token-exchange", "ghcr.io/wirepact/k8s-token-exchange-translator:latest" },
    };

    public WellKnownTranslators(ILogger<WellKnownTranslators> logger, IKubernetesClient client)
        : base(TimeSpan.FromMinutes(1))
    {
        _logger = logger;
        _client = client;
    }

    protected override async void Execute(object? _)
    {
        _logger.LogDebug("Check for well-known translators");
        var translators = await _client.List<V1Alpha1CredentialTranslator>();

        foreach (var (name, image) in Translators)
        {
            if (translators.Any(t => t.Name() == name))
            {
                continue;
            }

            _logger.LogInformation("Translator {name} not found, creating.", name);
            var translator = new V1Alpha1CredentialTranslator().Initialize();
            translator.EnsureMetadata().Name = name;
            translator.Spec.Image = image;
            await _client.Create(translator);
        }
    }
}
