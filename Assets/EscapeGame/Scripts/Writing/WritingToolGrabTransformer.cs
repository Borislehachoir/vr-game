using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace EscapeGame
{
    /// <summary>
    /// Prise en main standard (XRGeneralGrabTransformer) qui empêche en plus la pointe d'un WritingTool
    /// de traverser une WritableSurface : si la main va trop loin, l'outil est repoussé contre la surface
    /// et la pointe glisse dessus. Ajouté automatiquement par WritingTool au lancement.
    /// </summary>
    public class WritingToolGrabTransformer : XRGeneralGrabTransformer
    {
        [Tooltip("Distance (m) entre l'outil et une zone d'écriture en dessous de laquelle la zone le bloque.")]
        public float blockRange = 0.3f;

        WritingTool m_Tool;

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose, ref Vector3 localScale)
        {
            base.Process(grabInteractable, updatePhase, ref targetPose, ref localScale);

            if (m_Tool == null)
                m_Tool = GetComponent<WritingTool>();
            if (m_Tool == null || m_Tool.tip == null)
                return;

            // Position de la pointe par rapport à l'outil, appliquée à la pose voulue par la main.
            var tipOffset = Quaternion.Inverse(transform.rotation) * (m_Tool.tip.position - transform.position);

            foreach (var surface in WritableSurface.Active)
            {
                var tip = targetPose.position + targetPose.rotation * tipOffset;
                // transform.position = pose de l'image précédente, déjà corrigée : elle indique
                // de quel côté de la surface se trouve l'outil, même si la main est passée derrière.
                if (surface.TryGetPenetration(transform.position, tip, blockRange, out var push))
                    targetPose.position += push;
            }
        }
    }
}
