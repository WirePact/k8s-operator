using DotnetKubernetesClient.Entities;
using k8s.Models;
using KubeOps.Operator.Entities;
using KubeOps.Operator.Entities.Annotations;
using KubeOps.Operator.Rbac;

namespace Operator.Entities;

/// <summary>
/// Definition of a WirePact PKI. Only one PKI must be available at all time.
/// If multiple PKI definitions exist, the operator will throw errors and does not
/// configure the system.
/// </summary>
[KubernetesEntity(Group = "wirepact.ch", ApiVersion = "v1alpha1", Kind = "Pki")]
[EntityRbac(typeof(V1Alpha1Pki), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Create)]
[EntityScope(EntityScope.Cluster)]
public class V1Alpha1Pki : CustomKubernetesEntity<V1Alpha1Pki.V1Alpha1PkiSpec, V1Alpha1Pki.V1Alpha1PkiStatus>
{
    /// <summary>
    /// Specification for a v1alpha1 PKI.
    /// </summary>
    public class V1Alpha1PkiSpec
    {
        /// <summary>
        /// The container image that shall be used to start the pki deployment.
        /// Defaults to the k8s-pki image of WirePact with latest tag.
        /// </summary>
        public string Image { get; set; } = "ghcr.io/wirepact/k8s-pki:latest";

        /// <summary>
        /// The port on which the pki endpoints are reachable.
        /// Defaults to 8080.
        /// </summary>
        public int Port { get; set; } = 8080;

        /// <summary>
        /// The name of the Kubernetes secret that is used to store key material for the CA.
        /// Defaults to `wirepact-pki-ca`.
        /// </summary>
        public string SecretName { get; set; } = "wirepact-pki-ca";
    }

    /// <summary>
    /// Status of the PKI.
    /// </summary>
    public class V1Alpha1PkiStatus
    {
        /// <summary>
        /// Shows the actual address of the pki (service name with namespace).
        /// </summary>
        [AdditionalPrinterColumn]
        public string DnsAddress { get; set; } = string.Empty;
    }
}
