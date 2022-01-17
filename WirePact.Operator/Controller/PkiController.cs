using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Entities.Extensions;
using KubeOps.Operator.Events;
using KubeOps.Operator.Rbac;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WirePact.Operator.Entities;

namespace WirePact.Operator.Controller;

[EntityRbac(typeof(V1Alpha1Pki), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch | RbacVerb.Create)]
[EntityRbac(typeof(V1Deployment), typeof(V1Service), Verbs = RbacVerb.Get | RbacVerb.Create)]
[EntityRbac(typeof(V1Secret), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
internal class PkiController : IResourceController<V1Alpha1Pki>, IHostedService
{
    private readonly ILogger<PkiController> _logger;
    private readonly IKubernetesClient _client;
    private readonly IEventManager _eventManager;

    public PkiController(ILogger<PkiController> logger, IKubernetesClient client, IEventManager eventManager)
    {
        _logger = logger;
        _client = client;
        _eventManager = eventManager;
    }

    public async Task<ResourceControllerResult?> ReconcileAsync(V1Alpha1Pki pki)
    {
        var name = pki.Name();
        var @namespace = await _client.GetCurrentNamespace();
        var deployment = await _client.Get<V1Deployment>(name, @namespace);
        var service = await _client.Get<V1Service>(name, @namespace);
        var secret = await _client.Get<V1Secret>(pki.Spec.SecretName, @namespace);

        if (deployment != null && service != null && secret != null)
        {
            _logger.LogDebug(
                "Deployment, Secret and Service already exist for PKI '{name}' in namespace '{namespace}'.",
                name,
                @namespace);
            return null;
        }

        if (secret == null)
        {
            _logger.LogInformation(
                "Creating secret for PKI '{name}' in namespace '{namespace}'.",
                name,
                @namespace);
            secret = new V1Secret().Initialize().WithOwnerReference(pki);
            secret.Metadata.Name = pki.Spec.SecretName;
            secret.Metadata.NamespaceProperty = @namespace;
            secret.WriteData("serialNumber", "0");
            await _client.Create(secret);
            await _eventManager.PublishAsync(pki, "SECRET_CREATED", "Created the secret for CA storage.");
        }

        if (deployment == null)
        {
            _logger.LogInformation(
                "Creating deployment for PKI '{name}' in namespace '{namespace}'.",
                name,
                @namespace);

            try
            {
                await _client.Create(CreateDeployment(pki, @namespace));
                await _eventManager.PublishAsync(pki, "DEPLOYMENT_CREATED", "Created the deployment for the PKI.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not create deployment for PKI '{name}'.", name);
                await _eventManager.PublishAsync(
                    pki,
                    "DEPLOYMENT_FAILED",
                    $"Could not create deployment for PKI: {e}",
                    EventType.Warning);
                throw;
            }
        }

        if (service == null)
        {
            _logger.LogInformation(
                "Creating service for PKI '{name}' in namespace '{namespace}'.",
                name,
                @namespace);

            try
            {
                await _client.Create(CreateService(pki, @namespace));
                await _eventManager.PublishAsync(pki, "SERVICE_CREATED", "Created the service for the PKI.");
                pki.Status.DnsAddress = $"{pki.Name()}.{@namespace}:{pki.Spec.Port}";
                await _client.UpdateStatus(pki);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not create service for PKI '{name}'.", name);
                await _eventManager.PublishAsync(
                    pki,
                    "SERVICE_FAILED",
                    $"Could not create service for PKI: {e}",
                    EventType.Warning);
                pki.Status.DnsAddress = string.Empty;
                await _client.UpdateStatus(pki);
                throw;
            }
        }

        return null;
    }

    public Task DeletedAsync(V1Alpha1Pki entity) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // TODO: make this with a timer.
        var pkis = await _client.List<V1Alpha1Pki>();
        if (pkis.Any())
        {
            return;
        }

        var pki = new V1Alpha1Pki().Initialize();

        pki.Metadata.Name = "wirepact-pki";
        pki.SetLabel("app.kubernetes.io/name", "wirepact-pki");
        pki.SetLabel("app.kubernetes.io/part-of", "wirepact");
        pki.SetLabel("app.kubernetes.io/component", "pki");
        pki.SetLabel("app.kubernetes.io/managed-by", "wirepact-operator");
        pki.SetLabel("app.kubernetes.io/created-by", "wirepact-operator");

        await _client.Create(pki);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static V1Deployment CreateDeployment(V1Alpha1Pki pki, string @namespace)
    {
        var deployment = new V1Deployment().Initialize().WithOwnerReference(pki);

        deployment.Metadata.Name = pki.Name();
        deployment.Metadata.NamespaceProperty = @namespace;
        deployment.SetLabel("app.kubernetes.io/name", pki.Name());
        deployment.SetLabel("app.kubernetes.io/part-of", "wirepact");
        deployment.SetLabel("app.kubernetes.io/component", "pki");
        deployment.SetLabel("app.kubernetes.io/managed-by", pki.Name());
        deployment.SetLabel("app.kubernetes.io/created-by", "wirepact-operator");
        var podLabels = new Dictionary<string, string>
        {
            { "app.kubernetes.io/name", pki.Name() },
            { "app.kubernetes.io/component", "pki" },
            { "app.kubernetes.io/part-of", "wirepact" },
        };

        deployment.Spec = new V1DeploymentSpec
        {
            Selector = new V1LabelSelector(matchLabels: podLabels), RevisionHistoryLimit = 0,
        };

        var probe = new V1Probe
        {
            InitialDelaySeconds = 5, HttpGet = new V1HTTPGetAction { Port = pki.Spec.Port, Path = "/healthz" },
        };
        deployment.Spec.Template = new V1PodTemplateSpec
        {
            Metadata = new V1ObjectMeta { Labels = podLabels },
            Spec = new V1PodSpec
            {
                Containers = new List<V1Container>
                {
                    new()
                    {
                        Image = pki.Spec.Image,
                        ImagePullPolicy = "Always",
                        Name = pki.Name(),
                        Args = new List<string> { $"-port {pki.Spec.Port}", $"-secret {pki.Spec.SecretName}", },
                        Resources = new V1ResourceRequirements
                        {
                            Requests = new Dictionary<string, ResourceQuantity>
                            {
                                { "cpu", new ResourceQuantity("50m") },
                                { "memory", new ResourceQuantity("32Mi") },
                            },
                            Limits = new Dictionary<string, ResourceQuantity>
                            {
                                { "cpu", new ResourceQuantity("100m") },
                                { "memory", new ResourceQuantity("48Mi") },
                            },
                        },
                        ReadinessProbe = probe,
                        LivenessProbe = probe,
                        Ports = new List<V1ContainerPort> { new() { Name = "http", ContainerPort = pki.Spec.Port, }, },
                    },
                },
            },
        };

        deployment.Validate();

        return deployment;
    }

    private static V1Service CreateService(V1Alpha1Pki pki, string @namespace)
    {
        var service = new V1Service().Initialize().WithOwnerReference(pki);

        service.Metadata.Name = pki.Name();
        service.Metadata.NamespaceProperty = @namespace;
        service.SetLabel("app.kubernetes.io/name", pki.Name());
        service.SetLabel("app.kubernetes.io/part-of", "wirepact");
        service.SetLabel("app.kubernetes.io/component", "pki");
        service.SetLabel("app.kubernetes.io/managed-by", pki.Name());
        service.SetLabel("app.kubernetes.io/created-by", "wirepact-operator");

        service.Spec = new V1ServiceSpec
        {
            Ports = new List<V1ServicePort>
            {
                new() { Name = "http", Port = pki.Spec.Port, TargetPort = pki.Spec.Port, },
            },
            Selector = new Dictionary<string, string>
            {
                { "app.kubernetes.io/name", pki.Name() },
                { "app.kubernetes.io/component", "pki" },
                { "app.kubernetes.io/part-of", "wirepact" },
            },
        };

        service.Validate();

        return service;
    }
}
