using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Entities.Extensions;
using KubeOps.Operator.Events;
using KubeOps.Operator.Rbac;
using Operator.Entities;

namespace Operator.Controller;

[EntityRbac(typeof(V1Alpha1Pki), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch | RbacVerb.Create)]
[EntityRbac(
    typeof(V1Deployment),
    typeof(V1Service),
    typeof(V1ServiceAccount),
    typeof(V1Role),
    typeof(V1RoleBinding),
    Verbs = RbacVerb.Get | RbacVerb.Create)]
[EntityRbac(typeof(V1Secret), Verbs = RbacVerb.Get | RbacVerb.Create | RbacVerb.Update)]
internal class PkiController : IResourceController<V1Alpha1Pki>
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
        if (pki.Status.Namespace == null)
        {
            pki.Status.Namespace = await _client.GetCurrentNamespace();
            await _client.UpdateStatus(pki);
        }

        var @namespace = pki.Status.Namespace!;

        await UpsertServiceAccount(pki, @namespace);
        await UpsertRole(pki, @namespace);
        await UpsertRoleBinding(pki, @namespace);
        await UpsertSecret(pki, @namespace);
        await UpsertDeployment(pki, @namespace);
        await UpsertService(pki, @namespace);

        return null;
    }

    public Task DeletedAsync(V1Alpha1Pki entity) => Task.CompletedTask;

    private async Task UpsertServiceAccount(V1Alpha1Pki pki, string @namespace)
    {
        var serviceAccount = await _client.Get<V1ServiceAccount>(pki.Name(), @namespace);
        if (serviceAccount != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating service account for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);
        serviceAccount = new V1ServiceAccount().Initialize().WithOwnerReference(pki);
        serviceAccount.Metadata.Name = pki.Name();
        serviceAccount.Metadata.NamespaceProperty = @namespace;

        serviceAccount.Validate();

        try
        {
            await _client.Create(serviceAccount);
            await _eventManager.PublishAsync(pki, "SERVICE_ACCOUNT_CREATED", "Created the service account the PKI.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create service account for PKI '{name}'.", pki.Name());
            await _eventManager.PublishAsync(
                pki,
                "SERVICE_ACCOUNT_FAILED",
                $"Could not create secret for PKI: {e}",
                EventType.Warning);
            throw;
        }
    }

    private async Task UpsertRole(V1Alpha1Pki pki, string @namespace)
    {
        var role = await _client.Get<V1Role>(pki.Name(), @namespace);
        if (role != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating role for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);
        role = new V1Role().Initialize().WithOwnerReference(pki);
        role.Metadata.Name = pki.Name();
        role.Metadata.NamespaceProperty = @namespace;
        role.Rules = new List<V1PolicyRule>
        {
            new()
            {
                ApiGroups = new List<string> { string.Empty },
                Resources = new List<string> { "secrets" },
                Verbs = new List<string> { "get", "update" },
            },
        };

        role.Validate();

        try
        {
            await _client.Create(role);
            await _eventManager.PublishAsync(pki, "ROLE_CREATED", "Created the role the PKI.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create role for PKI '{name}'.", pki.Name());
            await _eventManager.PublishAsync(
                pki,
                "ROLE_FAILED",
                $"Could not create role for PKI: {e}",
                EventType.Warning);
            throw;
        }
    }

    private async Task UpsertRoleBinding(V1Alpha1Pki pki, string @namespace)
    {
        var roleBinding = await _client.Get<V1RoleBinding>(pki.Name(), @namespace);
        if (roleBinding != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating role binding for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);
        roleBinding = new V1RoleBinding().Initialize().WithOwnerReference(pki);
        roleBinding.Metadata.Name = pki.Name();
        roleBinding.Metadata.NamespaceProperty = @namespace;
        roleBinding.RoleRef = new() { Kind = "Role", ApiGroup = "rbac.authorization.k8s.io", Name = pki.Name() };
        roleBinding.Subjects = new List<V1Subject>
        {
            new() { Kind = "ServiceAccount", Name = pki.Name(), NamespaceProperty = @namespace, },
        };

        roleBinding.Validate();

        try
        {
            await _client.Create(roleBinding);
            await _eventManager.PublishAsync(pki, "ROLE_BINDING_CREATED", "Created the role binding the PKI.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create role binding for PKI '{name}'.", pki.Name());
            await _eventManager.PublishAsync(
                pki,
                "ROLE_BINDING_FAILED",
                $"Could not create role binding for PKI: {e}",
                EventType.Warning);
            throw;
        }
    }

    private async Task UpsertSecret(V1Alpha1Pki pki, string @namespace)
    {
        var secret = await _client.Get<V1Secret>(pki.Spec.SecretName, @namespace);
        if (secret != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating secret for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);
        secret = new V1Secret().Initialize().WithOwnerReference(pki);
        secret.Metadata.Name = pki.Spec.SecretName;
        secret.Metadata.NamespaceProperty = @namespace;
        secret.WriteData("serialNumber", "0");

        secret.Validate();

        try
        {
            await _client.Create(secret);
            await _eventManager.PublishAsync(pki, "SECRET_CREATED", "Created the secret for CA storage.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create secret for PKI '{name}'.", pki.Name());
            await _eventManager.PublishAsync(
                pki,
                "SECRET_FAILED",
                $"Could not create secret for PKI: {e}",
                EventType.Warning);
            throw;
        }
    }

    private async Task UpsertDeployment(V1Alpha1Pki pki, string @namespace)
    {
        var deployment = await _client.Get<V1Deployment>(pki.Name(), @namespace);
        if (deployment != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating deployment for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);

        deployment = new V1Deployment().Initialize().WithOwnerReference(pki);

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
            InitialDelaySeconds = 3, TcpSocket = new V1TCPSocketAction { Port = pki.Spec.Port },
        };
        deployment.Spec.Template = new V1PodTemplateSpec
        {
            Metadata = new V1ObjectMeta { Labels = podLabels },
            Spec = new V1PodSpec
            {
                ServiceAccountName = pki.Name(),
                Containers = new List<V1Container>
                {
                    new()
                    {
                        Image = pki.Spec.Image,
                        ImagePullPolicy = "Always",
                        Name = pki.Name(),
                        Args = new List<string> { $"--port={pki.Spec.Port}", $"--secret-name={pki.Spec.SecretName}", },
                        Resources = new V1ResourceRequirements
                        {
                            Requests = new Dictionary<string, ResourceQuantity>
                            {
                                { "cpu", new ResourceQuantity("50m") },
                                { "memory", new ResourceQuantity("16Mi") },
                            },
                            Limits = new Dictionary<string, ResourceQuantity>
                            {
                                { "cpu", new ResourceQuantity("50m") },
                                { "memory", new ResourceQuantity("32Mi") },
                            },
                        },
                        ReadinessProbe = probe,
                        LivenessProbe = probe,
                        Ports = new List<V1ContainerPort> { new() { Name = "grpc", ContainerPort = pki.Spec.Port, }, },
                    },
                },
            },
        };

        deployment.Validate();

        try
        {
            await _client.Create(deployment);
            await _eventManager.PublishAsync(pki, "DEPLOYMENT_CREATED", "Created the deployment for the PKI.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create deployment for PKI '{name}'.", pki.Name());
            await _eventManager.PublishAsync(
                pki,
                "DEPLOYMENT_FAILED",
                $"Could not create deployment for PKI: {e}",
                EventType.Warning);
            throw;
        }
    }

    private async Task UpsertService(V1Alpha1Pki pki, string @namespace)
    {
        var service = await _client.Get<V1Service>(pki.Name(), @namespace);
        if (service != null)
        {
            return;
        }

        _logger.LogInformation(
            "Creating service for PKI '{name}' in namespace '{namespace}'.",
            pki.Name(),
            @namespace);

        service = new V1Service().Initialize().WithOwnerReference(pki);

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
                new() { Name = "grpc", Port = pki.Spec.Port, TargetPort = pki.Spec.Port, },
            },
            Selector = new Dictionary<string, string>
            {
                { "app.kubernetes.io/name", pki.Name() },
                { "app.kubernetes.io/component", "pki" },
                { "app.kubernetes.io/part-of", "wirepact" },
            },
        };

        service.Validate();

        try
        {
            await _client.Create(service);
            await _eventManager.PublishAsync(pki, "SERVICE_CREATED", "Created the service for the PKI.");
            pki.Status.DnsAddress = $"{pki.Name()}.{@namespace}:{pki.Spec.Port}";
            await _client.UpdateStatus(pki);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not create service for PKI '{name}'.", pki.Name());
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
}
