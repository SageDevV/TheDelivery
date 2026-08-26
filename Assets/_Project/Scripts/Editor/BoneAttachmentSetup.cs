using System.Collections.Generic;
using TheDelivery.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Prende o objeto SELECIONADO ao bone da mão do personagem que o contém — o caso da
    /// xícara que precisa subir junto com a mão do idoso na animação de tomar café. Acha o
    /// bone sozinho, põe o <see cref="BoneAttachment"/> e captura como offset o encaixe que
    /// o objeto já tem, para ele não pular de lugar quando o componente entra.
    ///
    /// Uso: selecione na Hierarchy o objeto que deve seguir a mão (no caso, o "Hand" que
    /// segura a xícara — NÃO a xícara em si, para o offset valer para o grupo inteiro) e
    /// rode <c>Tools ▸ The Delivery ▸ Anim - Prender à Mão (Objeto Selecionado)</c>.
    ///
    /// Idempotente: rodar de novo em algo que já está preso só reatribui o bone e recaptura
    /// o offset da pose atual.
    /// </summary>
    public static class BoneAttachmentSetup
    {
        /// <summary>Pedaços de nome que denunciam o bone da mão, em rigs Mixamo e nacionais.</summary>
        private static readonly string[] HandNameHints = { "hand", "wrist", "mao", "mão", "palm" };

        /// <summary>
        /// Os DEDOS também têm "hand" no caminho e, em rig Mixamo, nomes como
        /// "mixamorig:RightHandIndex1" casam com o filtro acima. Prender a xícara na falange
        /// do indicador quase funciona — e é um inferno pra descobrir depois por que o
        /// encaixe treme.
        /// </summary>
        private static readonly string[] FingerNameHints =
        {
            "index", "thumb", "middle", "ring", "pinky", "finger", "dedo", "polegar"
        };

        [MenuItem("Tools/The Delivery/Anim - Prender à Mão (Objeto Selecionado)")]
        public static void AttachSelectedToHand()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length == 0)
            {
                Debug.LogWarning("[BoneAttachment] Selecione na Hierarchy o objeto que deve seguir a mão " +
                                 "(ex.: o 'Hand' que segura a xícara).");
                return;
            }

            int attached = 0;

            foreach (GameObject target in selection)
            {
                if (target == null || EditorUtility.IsPersistent(target))
                    continue;

                Animator animator = target.GetComponentInParent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning($"[BoneAttachment] '{target.name}' não está dentro de nenhum personagem com " +
                                     "Animator. Ele precisa ser filho (em algum nível) do objeto animado.", target);
                    continue;
                }

                Transform hand = FindHandBone(animator.transform, target.transform);
                if (hand == null)
                {
                    Debug.LogWarning($"[BoneAttachment] Não achei bone de mão dentro de '{animator.name}'. " +
                                     "Adicione o BoneAttachment na mão e arraste o bone certo pro campo 'Bone'.", target);
                    continue;
                }

                BoneAttachment attachment = target.GetComponent<BoneAttachment>();
                if (attachment == null)
                    attachment = Undo.AddComponent<BoneAttachment>(target);
                else
                    Undo.RecordObject(attachment, "Prender à mão");

                attachment.Bone = hand;

                // Captura ANTES de qualquer LateUpdate mexer no objeto: o offset gravado é o
                // encaixe que o usuário já montou na mão, então nada salta de lugar quando o
                // componente começa a agir.
                attachment.CaptureOffset();

                EditorUtility.SetDirty(attachment);
                attached++;

                Debug.Log($"[BoneAttachment] '{target.name}' preso ao bone '{hand.name}' de '{animator.name}'.", target);
            }

            if (attached > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            if (attached > 0)
                Debug.Log($"[BoneAttachment] {attached} objeto(s) preso(s). Dê Play pra conferir o encaixe; " +
                          "pra corrigir a pega, mexa em Position/Rotation Offset no Inspector.");
        }

        /// <summary>
        /// Acha o bone da mão dentro do esqueleto. Entre os candidatos, escolhe o MAIS PERTO
        /// do objeto: é o que resolve sozinho a pergunta "mão direita ou esquerda?", já que o
        /// usuário já posicionou a xícara do lado certo antes de chamar o comando.
        /// </summary>
        private static Transform FindHandBone(Transform skeletonRoot, Transform target)
        {
            var candidates = new List<Transform>();

            foreach (Transform candidate in skeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                // O próprio objeto costuma se chamar "Hand" (foi assim que o usuário nomeou o
                // porta-xícara). Sem esta guarda ele se prenderia em si mesmo, o encaixe
                // ficaria imóvel, e o sintoma seria idêntico ao problema original.
                if (candidate == target || candidate.IsChildOf(target))
                    continue;

                if (!IsHandName(candidate.name) || IsFingerName(candidate.name))
                    continue;

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
                return null;

            Transform best = null;
            float bestDistance = float.MaxValue;

            foreach (Transform candidate in candidates)
            {
                float distance = Vector3.SqrMagnitude(candidate.position - target.position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private static bool IsHandName(string name) => ContainsAny(name, HandNameHints);

        private static bool IsFingerName(string name) => ContainsAny(name, FingerNameHints);

        private static bool ContainsAny(string name, string[] hints)
        {
            string lower = name.ToLowerInvariant();

            foreach (string hint in hints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            return false;
        }
    }
}
