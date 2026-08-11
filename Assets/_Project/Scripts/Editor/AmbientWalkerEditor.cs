using TheDelivery.AI;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Inspector do <see cref="AmbientWalker"/> com o botão que MEDE a passada do clipe de
    /// caminhada e preenche o 'Clip Stride Speed'.
    ///
    /// Sem esse número o 'Walk Speed' é um chute: o NPC atravessa o cenário numa velocidade que
    /// não tem relação com o tamanho do passo que o animador desenhou, e o pé desliza no chão.
    /// A medição tira o valor do próprio clipe, então o acerto deixa de ser no olho.
    /// </summary>
    [CustomEditor(typeof(AmbientWalker))]
    public sealed class AmbientWalkerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Calibração da caminhada", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Lê o deslocamento embutido no clipe e escreve a velocidade dele em " +
                "'Clip Stride Speed'. A partir daí o Animator acompanha o 'Walk Speed' sozinho.",
                EditorStyles.wordWrappedLabel);

            if (GUILayout.Button("Medir passada do clipe", GUILayout.Height(24)))
                Measure((AmbientWalker)target);
        }

        private void Measure(AmbientWalker walker)
        {
            Animator animator = walker.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                EditorUtility.DisplayDialog("Medir passada",
                    "Não achei um Animator com controller neste NPC.\n\nGere o controller primeiro " +
                    "(Tools ▸ The Delivery ▸ Build Looping Clip Controller).", "OK");
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0 || clips[0] == null || clips[0].length <= 0f)
            {
                EditorUtility.DisplayDialog("Medir passada",
                    "O controller não tem nenhum clipe com duração.", "OK");
                return;
            }

            AnimationClip clip = clips[0];

            if (!TryMeasure(clip, walker.transform, out float speed, out string source))
            {
                EditorUtility.DisplayDialog("Medir passada",
                    $"O clipe \"{clip.name}\" não tem deslocamento nenhum embutido — ele já foi " +
                    "exportado 'in place'.\n\nNesse caso não dá para deduzir o tamanho do passo do " +
                    "arquivo: ajuste o 'Walk Speed' no olho até o pé parar de escorregar, ou " +
                    "reexporte o clipe do Mixamo SEM 'In Place'.", "OK");
                return;
            }

            SerializedProperty stride = serializedObject.FindProperty("clipStrideSpeed");
            SerializedProperty walkSpeed = serializedObject.FindProperty("walkSpeed");

            stride.floatValue = speed;
            serializedObject.ApplyModifiedProperties();

            float rate = walkSpeed.floatValue / speed;
            EditorUtility.DisplayDialog("Medir passada",
                $"Clipe \"{clip.name}\" ({clip.length:0.00}s)\n" +
                $"Passada medida em {source}: {speed:0.000} m/s\n\n" +
                $"Com 'Walk Speed' = {walkSpeed.floatValue:0.00} m/s o Animator vai rodar a " +
                $"{rate:0.00}x.", "OK");
        }

        /// <summary>
        /// Descobre quantos metros por segundo o clipe anda sozinho. Tenta pelos dois lugares
        /// onde esse deslocamento pode estar, porque depende de como o FBX foi importado:
        /// <list type="number">
        /// <item>com 'Root node' definido no Rig, a translação foi EXTRAÍDA para as curvas de
        /// root motion do clipe — é o que <see cref="AnimationClip.averageSpeed"/> devolve;</item>
        /// <item>sem 'Root node', ela continua crua dentro do osso do quadril, e só aparece
        /// lendo a curva de posição desse osso.</item>
        /// </list>
        /// Só o plano horizontal conta: o Y é o corpo subindo e descendo no passo, não avanço.
        /// </summary>
        private static bool TryMeasure(AnimationClip clip, Transform npcRoot, out float speed, out string source)
        {
            speed = 0f;
            source = null;

            Vector3 average = clip.averageSpeed;
            float horizontal = new Vector2(average.x, average.z).magnitude;
            if (horizontal > 0.001f)
            {
                speed = horizontal;
                source = "root motion do clipe";
                return true;
            }

            Transform hips = FindRootBone(npcRoot);
            if (hips == null)
                return false;

            string path = AnimationUtility.CalculateTransformPath(hips, npcRoot);
            float dx = CurveTravel(clip, path, "m_LocalPosition.x");
            float dz = CurveTravel(clip, path, "m_LocalPosition.z");
            float distance = new Vector2(dx, dz).magnitude;

            if (distance <= 0.001f)
                return false;

            speed = distance / clip.length;
            source = $"osso \"{hips.name}\"";
            return true;
        }

        /// <summary>Quanto a curva andou entre o primeiro e o último frame do clipe.</summary>
        private static float CurveTravel(AnimationClip clip, string path, string property)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path || binding.propertyName != property)
                    continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    return 0f;

                return curve.Evaluate(clip.length) - curve.Evaluate(0f);
            }

            return 0f;
        }

        /// <summary>
        /// O osso que carrega a translação: o 'Root Bone' do SkinnedMeshRenderer (mixamorig:Hips
        /// nos modelos do projeto).
        /// </summary>
        private static Transform FindRootBone(Transform npcRoot)
        {
            var skinned = npcRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            return skinned != null ? skinned.rootBone : null;
        }
    }
}
