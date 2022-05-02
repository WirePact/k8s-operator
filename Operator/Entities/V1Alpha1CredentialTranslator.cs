using DotnetKubernetesClient.Entities;
using k8s.Models;
using KubeOps.Operator.Entities;
using KubeOps.Operator.Entities.Annotations;
using KubeOps.Operator.Rbac;

namespace Operator.Entities;

/// <summary>
/// Definition of a credential translator. Translators are cluster wide and the definitions
/// are needed to define which translators are allowed in the wirepact mesh.
/// </summary>
[KubernetesEntity(Group = "wirepact.ch", ApiVersion = "v1alpha1", Kind = "CredentialTranslator")]
[EntityRbac(typeof(V1Alpha1CredentialTranslator), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Create)]
[EntityScope(EntityScope.Cluster)]
public class
    V1Alpha1CredentialTranslator : CustomKubernetesEntity<V1Alpha1CredentialTranslator.V1Alpha1CredentialTranslatorSpec>
{
    /// <summary>
    /// Specification of the translator.
    /// </summary>
    public class V1Alpha1CredentialTranslatorSpec
    {
        /// <summary>
        /// The container image to be used.
        /// </summary>
        [AdditionalPrinterColumn]
        public string Image { get; set; } = string.Empty;
    }
}
