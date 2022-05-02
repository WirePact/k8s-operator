// using DotnetKubernetesClient;
// using KubeOps.Operator.Rbac;
// using KubeOps.Operator.Webhooks;
// using Operator.Entities;
//
// namespace Operator.Webhooks;
//
// [EntityRbac(typeof(V1Alpha1CredentialTranslator), Verbs = RbacVerb.Get)]
// public class ParticipantValidator : IValidationWebhook<V1Alpha1MeshParticipant>
// {
//     private readonly IKubernetesClient _client;
//     private readonly ILogger<ParticipantValidator> _logger;
//
//     public ParticipantValidator(ILogger<ParticipantValidator> logger, IKubernetesClient client)
//     {
//         _logger = logger;
//         _client = client;
//     }
//
//     public AdmissionOperations Operations => AdmissionOperations.Create | AdmissionOperations.Update;
//
//     public Task<ValidationResult> CreateAsync(V1Alpha1MeshParticipant newEntity, bool dryRun) =>
//         CheckMeshParticipant(newEntity);
//
//     public Task<ValidationResult> UpdateAsync(
//         V1Alpha1MeshParticipant oldEntity,
//         V1Alpha1MeshParticipant newEntity,
//         bool dryRun) => CheckMeshParticipant(newEntity);
//
//     private async Task<ValidationResult> CheckMeshParticipant(V1Alpha1MeshParticipant participant)
//     {
//         var translator = participant.Spec.Translator;
//         _logger.LogDebug(@"Check for translator ""{translator}"".", translator);
//
//         var definedTranslator = await _client.Get<V1Alpha1CredentialTranslator>(translator);
//
//         if (definedTranslator == null)
//         {
//             _logger.LogWarning(@"Translator ""{translator}"" could not be found in cluster.", translator);
//             return ValidationResult.Fail(
//                 StatusCodes.Status400BadRequest,
//                 @$"No translator definition could be found for ""{translator}"".");
//         }
//
//         if (participant.Spec.Deployment == string.Empty)
//         {
//             return ValidationResult.Fail(
//                 StatusCodes.Status400BadRequest,
//                 "There is no deployment set in the mesh participant.");
//         }
//
//         if (participant.Spec.Service == string.Empty)
//         {
//             return ValidationResult.Fail(
//                 StatusCodes.Status400BadRequest,
//                 "There is no service set in the mesh participant.");
//         }
//
//         return ValidationResult.Success();
//     }
// }
