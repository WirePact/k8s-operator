using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Entities.Extensions;
using KubeOps.Operator.Rbac;
using Microsoft.Extensions.Logging;
using WirePact.Operator.Entities;

namespace WirePact.Operator.Controller;

/// <summary>
/// Controller that updates deployments/services for mesh participants.
/// </summary>
[EntityRbac(typeof(V1Alpha1CredentialTranslator), typeof(V1Alpha1Pki), Verbs = RbacVerb.Get)]
[EntityRbac(
    typeof(V1Deployment),
    typeof(V1Service),
    typeof(V1ConfigMap),
    Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
[EntityRbac(typeof(V1Alpha1MeshParticipant), Verbs = RbacVerb.Get | RbacVerb.Watch | RbacVerb.Update)]
public class ParticipantController : IResourceController<V1Alpha1MeshParticipant>
{
    private static readonly Random Random = new();

    private const int LowerPort = 40000;
    private const int UpperPort = 60000;
    private const string EnvoyContainerName = "wirepact-envoy";
    private const string TranslatorContainerName = "wirepact-translator";
    private const string ConfigVolumeName = "wirepact-envoy-config";
    private const string EnvoyImage = "envoyproxy/envoy-alpine:v1.20-latest";

    private readonly ILogger<ParticipantController> _logger;
    private readonly IKubernetesClient _client;

    /// <summary>
    /// Ctor.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="client"></param>
    public ParticipantController(ILogger<ParticipantController> logger, IKubernetesClient client)
    {
        _logger = logger;
        _client = client;
    }

    /// <inheritdoc />
    public async Task<ResourceControllerResult?> ReconcileAsync(V1Alpha1MeshParticipant participant)
    {
        _logger.LogInformation(
            @"Checking mesh participant ""{name}"" with id ""{id}"".",
            participant.Name(),
            participant.Uid());

        var translator = await _client.Get<V1Alpha1CredentialTranslator>(participant.Spec.Translator);
        if (translator == null)
        {
            _logger.LogError(
                @"Found no translator ""{translator}"" for participant ""{participant}"".",
                participant.Spec.Translator,
                participant.Name());
            throw new Exception(@$"No translator for participant ""{participant.Name()}"" found.");
        }

        var deployment = await _client.Get<V1Deployment>(participant.Spec.Deployment, participant.Namespace());
        if (deployment == null)
        {
            _logger.LogError(
                @"Found no deployment ""{deployment}"" for participant ""{participant}"".",
                participant.Spec.Deployment,
                participant.Name());
            throw new Exception(@$"No deployment for participant ""{participant.Name()}"" found.");
        }

        _logger.LogDebug(
            @"Found deployment ""{deployment}"" with id ""{id}"" for participant ""{participant}"".",
            deployment.Name(),
            deployment.Uid(),
            participant.Name());

        await CheckDeployment(participant, deployment, translator);

        var service = await _client.Get<V1Service>(participant.Spec.Service, participant.Namespace());
        if (service == null)
        {
            _logger.LogError(
                @"Found no service ""{service}"" for participant ""{participant}"".",
                participant.Spec.Service,
                participant.Name());
            throw new Exception(@$"No service for participant ""{participant.Name()}"" found.");
        }

        _logger.LogDebug(
            @"Found service ""{service}"" with id ""{id}"" for participant ""{participant}"".",
            service.Name(),
            service.Uid(),
            participant.Name());

        var appPort = service.Spec.Ports?.FirstOrDefault(p => p.Port == participant.Spec.TargetPort);
        if (appPort == null)
        {
            _logger.LogError(
                @"Found no service ""{service}"" for participant ""{participant}"".",
                participant.Spec.Service,
                participant.Name());
            throw new Exception(
                @$"Service ""{service.Name()}"" for participant ""{participant.Name()}"" has no app target port.");
        }

        if (appPort.TargetPort != "ingress")
        {
            _logger.LogInformation(@"Changing port of service ""{service}"" to ingress port.", service.Name());
            service.SetAnnotation("wirepact.ch/original-target-port", appPort.TargetPort);
            appPort.TargetPort = "ingress";
            await _client.Update(service);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task DeletedAsync(V1Alpha1MeshParticipant participant)
    {
        // remove all additions from the deployment
        var deployment = await _client.Get<V1Deployment>(participant.Spec.Deployment, participant.Namespace());
        if (deployment == null)
        {
            _logger.LogError(
                @"Found no deployment ""{deployment}"" for participant ""{participant}"".",
                participant.Spec.Deployment,
                participant.Name());
        }
        else
        {
            _logger.LogInformation(
                @"Remove additions from deployment ""{deployment}"" for participant ""{participant}"".",
                participant.Spec.Deployment,
                participant.Name());
            _logger.LogDebug(
                @"Remove translator/envoy container from deployment ""{deployment}"" for participant ""{participant}"".",
                participant.Spec.Deployment,
                participant.Name());
            deployment.Spec.Template.Spec.Containers = deployment.Spec.Template.Spec.Containers?.Where(
                    c => c.Name != TranslatorContainerName && c.Name != EnvoyContainerName)
                .ToList();

            if (deployment.Spec.Template.Spec.Containers != null)
            {
                _logger.LogDebug(
                    @"Remove HTTP_PROXY env from deployment ""{deployment}"" for participant ""{participant}"".",
                    participant.Spec.Deployment,
                    participant.Name());
                foreach (var container in deployment.Spec.Template.Spec.Containers.Where(
                             c => c.Env != null && c.Env.Any(env => env.Name == "HTTP_PROXY")))
                {
                    container.Env = container.Env.Where(e => e.Name != "HTTP_PROXY").ToList();
                }
            }

            _logger.LogDebug(
                @"Remove ConfigMapVolume from deployment ""{deployment}"" for participant ""{participant}"".",
                participant.Spec.Deployment,
                participant.Name());
            deployment.Spec.Template.Spec.Volumes =
                deployment.Spec.Template.Spec.Volumes?.Where(v => v.Name != ConfigVolumeName).ToList();

            await _client.Update(deployment);
        }

        // reset the service port
        var service = await _client.Get<V1Service>(participant.Spec.Service, participant.Namespace());
        if (service == null)
        {
            _logger.LogError(
                @"Found no service ""{service}"" for participant ""{participant}"".",
                participant.Spec.Service,
                participant.Name());
            return;
        }

        var originalPort = service.GetAnnotation("wirepact.ch/original-target-port");
        if (originalPort == null)
        {
            _logger.LogError(
                @"Found no original port on ""{service}"" for participant ""{participant}"".",
                participant.Spec.Service,
                participant.Name());
            return;
        }

        var ingressPort = service.Spec.Ports?.FirstOrDefault(p => p.TargetPort == "ingress");
        if (ingressPort == null)
        {
            _logger.LogError(
                @"Found no ingress port on ""{service}"" for participant ""{participant}"".",
                participant.Spec.Service,
                participant.Name());
            return;
        }

        service.SetAnnotation("wirepact.ch/original-target-port", null);
        ingressPort.TargetPort = originalPort;
        await _client.Update(service);
    }

    private static int RandomPort(IReadOnlyList<int> usedPorts)
    {
        int newPort;
        do
        {
            newPort = Random.Next(LowerPort, UpperPort);
        }
        while (usedPorts.Contains(newPort));

        return newPort;
    }

    private async Task<PortCollection> GetPorts(
        V1Alpha1MeshParticipant participant,
        V1Deployment deployment)
    {
        var participantStatusUpdated = false;

        var usedPorts = deployment.Spec.Template.Spec.Containers
            .SelectMany(c => c.Ports ?? new List<V1ContainerPort>())
            .Select(p => p.ContainerPort)
            .ToList();
        var ingressPort = participant.Status.IngressPort;
        var egressPort = participant.Status.EgressPort;
        var translatorIngressPort = participant.Status.TranslatorIngressPort;
        var translatorEgressPort = participant.Status.TranslatorEgressPort;

        if (ingressPort == default)
        {
            participant.Status.IngressPort = ingressPort = RandomPort(usedPorts);
            participantStatusUpdated = true;
        }

        usedPorts.Add(ingressPort);

        if (egressPort == default)
        {
            participant.Status.EgressPort = egressPort = RandomPort(usedPorts);
            participantStatusUpdated = true;
        }

        usedPorts.Add(egressPort);

        if (translatorIngressPort == default)
        {
            participant.Status.TranslatorIngressPort = translatorIngressPort = RandomPort(usedPorts);
            participantStatusUpdated = true;
        }

        usedPorts.Add(translatorIngressPort);

        if (translatorEgressPort == default)
        {
            participant.Status.TranslatorEgressPort = translatorEgressPort = RandomPort(usedPorts);
            participantStatusUpdated = true;
        }

        if (participantStatusUpdated)
        {
            await _client.UpdateStatus(participant);
        }

        return new PortCollection(ingressPort, egressPort, translatorIngressPort, translatorEgressPort);
    }

    private async Task<string> PkiAddress()
    {
        var pki = await _client.Get<V1Alpha1Pki>("wirepact-pki");
        if (pki == null)
        {
            throw new Exception("PKI must not be null");
        }

        return $"http://{pki.Status.DnsAddress}";
    }

    private async Task CheckDeployment(
        V1Alpha1MeshParticipant participant,
        V1Deployment deployment,
        V1Alpha1CredentialTranslator translator)
    {
        var deploymentUpdated = false;

        var (ingressPort, egressPort, translatorIngressPort, translatorEgressPort) =
            await GetPorts(participant, deployment);

        var pkiAddress = await PkiAddress();
        _logger.LogTrace("Got PKI Address from PKI: {address}", pkiAddress);

        var translatorContainer =
            deployment.Spec.Template.Spec.Containers.FirstOrDefault(c => c.Name == TranslatorContainerName);
        // first check the translator
        if (translatorContainer == null)
        {
            _logger.LogInformation(@"Adding translator sidecar to deployment ""{deployment}"".", deployment.Name());

            // no translator container is configured.
            deployment.Spec.Template.Spec.Containers.Add(
                new V1Container
                {
                    Name = TranslatorContainerName,
                    Image = translator.Spec.Image,
                    Ports = new List<V1ContainerPort> { new(translatorIngressPort), new(translatorEgressPort), },
                    Env = participant.Spec.Env
                        .Select(data => new V1EnvVar(data.Key, data.Value))
                        .Concat(
                            new[]
                            {
                                new V1EnvVar("COMMON_NAME", $"{participant.Spec.Translator}-{participant.Name()}"),
                                new V1EnvVar("INGRESS_PORT", translatorIngressPort.ToString()),
                                new V1EnvVar("EGRESS_PORT", translatorEgressPort.ToString()),
                                new V1EnvVar("PKI_ADDRESS", pkiAddress),
                            })
                        .ToList(),
                });
            _logger.LogDebug(
                @"Added translator container with common name ""{common_name}"", ingress port ""{ingressPort}"", egress port ""{egressPort}"" and pki address ""{pkiAddress}"".",
                $"{participant.Spec.Translator}-{participant.Name()}",
                translatorIngressPort.ToString(),
                translatorEgressPort.ToString(),
                pkiAddress);
            deploymentUpdated = true;
        }
        else
        {
            _logger.LogDebug(@"Checking translator sidecar of deployment ""{deployment}"".", deployment.Name());

            // check if the translator is configured correctly
            translatorContainer.Image = translator.Spec.Image;
            deploymentUpdated |= translatorContainer.Image != translator.Spec.Image;
            deploymentUpdated |= translatorContainer.EnsureEnvVar(
                "COMMON_NAME",
                $"{participant.Spec.Translator}-{participant.Name()}");
            deploymentUpdated |= translatorContainer.EnsureEnvVar("INGRESS_PORT", translatorIngressPort.ToString());
            deploymentUpdated |= translatorContainer.EnsureEnvVar("EGRESS_PORT", translatorEgressPort.ToString());
            deploymentUpdated |= translatorContainer.EnsureEnvVar("PKI_ADDRESS", pkiAddress);
            deploymentUpdated |= translatorContainer.EnsurePort(translatorIngressPort);
            deploymentUpdated |= translatorContainer.EnsurePort(translatorEgressPort);
        }

        // check if the envoy configmap is available
        var config = await _client.Get<V1ConfigMap>($"envoy-{participant.Name()}", participant.Namespace());
        var (envoyConfig, envoyConfigHash) = EnvoyConfig.Bootstrap(
            ingressPort,
            egressPort,
            participant.Spec.TargetPort,
            translatorIngressPort,
            translatorEgressPort);
        if (config == null)
        {
            _logger.LogInformation(@"Creating envoy configMap for participant ""{participant}"".", participant.Name());
            config = new V1ConfigMap().Initialize();
            config.Metadata.Name = $"envoy-{participant.Name()}";
            config.Metadata.NamespaceProperty = participant.Namespace();
            config.AddOwnerReference(participant.MakeOwnerReference());
            config.Data = new Dictionary<string, string>
            {
                { "envoy-config.yaml", envoyConfig }, { "config-hash", envoyConfigHash }
            };
            await _client.Create(config);
        }
        else
        {
            var found = config.Data.TryGetValue("config-hash", out var dataHash);
            if (!found || dataHash != envoyConfigHash)
            {
                _logger.LogInformation(
                    @"Updating envoy configMap for participant ""{participant}"".",
                    participant.Name());
                config.Data["envoy-config.yaml"] = envoyConfig;
                config.Data["config-hash"] = envoyConfigHash;
                await _client.Update(config);
            }
        }

        // check if the configmap is defined as volume.
        if (deployment.Spec.Template.Spec.Volumes?.Any(v => v.Name == ConfigVolumeName) != true)
        {
            _logger.LogInformation(
                @"Add envoy configMap to deployment volumes in deployment ""{deployment}"".",
                deployment.Name());

            deployment.Spec.Template.Spec.Volumes ??= new List<V1Volume>();
            deployment.Spec.Template.Spec.Volumes.Add(
                new V1Volume
                {
                    Name = ConfigVolumeName, ConfigMap = new V1ConfigMapVolumeSource(name: config.Name()),
                });

            deploymentUpdated = true;
        }
        else
        {
            _logger.LogDebug("Envoy ConfigMap volume mapping is intact.");
        }

        // check if an envoy container is defined
        var envoyContainer =
            deployment.Spec.Template.Spec.Containers.FirstOrDefault(c => c.Name == EnvoyContainerName);
        if (envoyContainer == null)
        {
            _logger.LogInformation(@"Adding envoy sidecar to deployment ""{deployment}"".", deployment.Name());

            // no envoy container is configured.
            deployment.Spec.Template.Spec.Containers.Add(
                new V1Container
                {
                    Name = EnvoyContainerName,
                    Image = EnvoyImage,
                    Ports = new List<V1ContainerPort>
                    {
                        new(Convert.ToInt32(ingressPort), name: "ingress"), new(egressPort),
                    },
                    Env = new List<V1EnvVar> { new("ENVOY_CONFIG_HASH", envoyConfigHash), },
                    VolumeMounts = new List<V1VolumeMount>
                    {
                        new(
                            "/config/envoy.yaml",
                            ConfigVolumeName,
                            readOnlyProperty: true,
                            subPath: "envoy-config.yaml"),
                    },
                    Command = new[] { "envoy", "-c", "/config/envoy.yaml" },
                });

            deploymentUpdated = true;
        }
        else
        {
            _logger.LogDebug(@"Checking envoy sidecar of deployment ""{deployment}"".", deployment.Name());

            envoyContainer.Image = EnvoyImage;
            deploymentUpdated |= envoyContainer.Image != EnvoyImage;
            deploymentUpdated |= envoyContainer.EnsurePort(ingressPort, "ingress");
            deploymentUpdated |= envoyContainer.EnsurePort(egressPort);
            deploymentUpdated |= envoyContainer.EnsureEnvVar("ENVOY_CONFIG_HASH", envoyConfigHash);

            if (!envoyContainer.VolumeMounts.All(
                    vm => vm.MountPath == "/config/envoy.yaml" && vm.Name == ConfigVolumeName))
            {
                envoyContainer.VolumeMounts = new List<V1VolumeMount>
                {
                    new(
                        "/config/envoy.yaml",
                        ConfigVolumeName,
                        readOnlyProperty: true,
                        subPath: "envoy-config.yaml"),
                };
                deploymentUpdated = true;
            }
        }

        // check if all other contains have the HTTP_PROXY env variable
        foreach (var container in deployment.Spec.Template.Spec.Containers.Where(
                     c => c.Name != TranslatorContainerName &&
                          c.Name != EnvoyContainerName &&
                          (c.Env == null || !c.Env.Any(env => env.Name == "HTTP_PROXY"))))
        {
            container.Env ??= new List<V1EnvVar>();
            container.Env.Add(new V1EnvVar("HTTP_PROXY", $"http://localhost:{egressPort}"));

            deploymentUpdated = true;
        }

        if (deploymentUpdated)
        {
            await _client.Update(deployment);
        }
    }

    private record struct PortCollection(
        int IngressPort,
        int EgressPort,
        int TranslatorIngressPort,
        int TranslatorEgressPort);
}
