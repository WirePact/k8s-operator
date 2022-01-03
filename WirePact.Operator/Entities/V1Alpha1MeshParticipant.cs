using System.Collections.Generic;
using k8s.Models;
using KubeOps.Operator.Entities;
using KubeOps.Operator.Entities.Annotations;

namespace WirePact.Operator.Entities;

/// <summary>
/// Mesh participant of the distributed
/// </summary>
[KubernetesEntity(Group = "wirepact.ch", ApiVersion = "v1alpha1", Kind = "MeshParticipant")]
public class V1Alpha1MeshParticipant : CustomKubernetesEntity<V1Alpha1MeshParticipant.V1Alpha1MeshParticipantSpec,
    V1Alpha1MeshParticipant.V1Alpha1MeshParticipantStatus>
{
    /// <summary>
    /// Definition of the mesh participant. Defines the used translator
    /// as well as the run configuration of the translator.
    /// </summary>
    public class V1Alpha1MeshParticipantSpec
    {
        /// <summary>
        /// Name reference to the <see cref="V1Deployment"/> that deploys the participant.
        /// The deployment will be updated and gets injected sidecars for the mesh to function.
        /// A valid participant must adhere to the HTTP_PROXY environment variable.
        /// </summary>
        [AdditionalPrinterColumn]
        public string Deployment { get; set; } = string.Empty;

        /// <summary>
        /// The port of the deployed application that is the original target port.
        /// </summary>
        public int TargetPort { get; set; }

        /// <summary>
        /// Name reference to the <see cref="V1Service"/> that makes the participant
        /// available for communication.
        /// </summary>
        [AdditionalPrinterColumn]
        public string Service { get; set; } = string.Empty;

        /// <summary>
        /// Defines the <see cref="V1Alpha1CredentialTranslator"/> that shall be used
        /// to translate the ingress/egress communication for the authentication mesh.
        /// If no translator is available, the object is rejected.
        /// </summary>
        [AdditionalPrinterColumn]
        public string Translator { get; set; } = string.Empty;

        /// <summary>
        /// Defines the environment variables that are passed to the translator for configuration.
        /// </summary>
        public IDictionary<string, string> Env { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Defines additional arguments that are passed to the translator for configuration.
        /// </summary>
        public IList<string> Args { get; set; } = new List<string>();
    }

    /// <summary>
    /// The status of the mesh participant.
    /// </summary>
    public class V1Alpha1MeshParticipantStatus
    {
        /// <summary>
        /// The port that listens for incoming communication.
        /// This is set via "INGRESS_PORT" environment variable.
        /// </summary>
        public int IngressPort { get; set; }

        /// <summary>
        /// The port that listens for outgoing communication.
        /// This is set via "EGRESS_PORT" environment variable.
        /// </summary>
        public int EgressPort { get; set; }

        /// <summary>
        /// The port that the translator listens for incoming requests.
        /// </summary>
        public int TranslatorIngressPort { get; set; }

        /// <summary>
        /// The port that the translator listens for outgoing requests.
        /// </summary>
        public int TranslatorEgressPort { get; set; }
    }
}
