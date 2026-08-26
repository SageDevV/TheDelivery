using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Ajusta um CapsuleCollider ao TAMANHO REAL do modelo selecionado, medido pelos bounds
    /// dos renderers.
    ///
    /// EXISTE POR CAUSA DA ESCALA. Os campos de um CapsuleCollider são LOCAIS: a Unity
    /// multiplica Center, Radius e Height pela escala do transform. Num modelo escalado
    /// ~210x — o caso das criaturas deste projeto, FBX Mixamo importado com useFileScale —
    /// a cápsula padrão (raio 0,5, altura 2) vira uma cápsula de 105 m de raio e 420 m de
    /// altura, e um Center Y de 1 a joga 210 m para cima. O resultado é um gizmo em outro
    /// bairro, que é exatamente o sintoma que este comando resolve.
    ///
    /// E não é só a escala: o pivô de um FBX Mixamo fica na origem do arquivo (entre os
    /// pés), não no meio do corpo. Por isso o centro sai dos BOUNDS e não do pivô — mirar
    /// no pivô deixaria a cápsula com metade do corpo enterrada no chão.
    ///
    /// Uso: selecione o objeto (ou vários) e rode
    /// <c>Tools ▸ The Delivery ▸ Colisor - Ajustar Cápsula ao Modelo (Selecionados)</c>.
    /// O componente é criado se não existir e SOBRESCRITO se existir — este comando é o
    /// oposto do setup da câmera do ataque: aqui não há ajuste fino a preservar, o valor
    /// certo é o que a medida diz.
    /// </summary>
    public static class CapsuleColliderSetup
    {
        // Folga lateral: os bounds de um personagem em pose de bind incluem os braços
        // abertos, e uma cápsula com a largura DELES seria larga demais para o corpo. A
        // altura não leva folga — ela é o que é.
        private const float RadiusFactor = 0.5f;

        [MenuItem("Tools/The Delivery/Colisor - Ajustar Cápsula ao Modelo (Selecionados)")]
        private static void Run()
        {
            GameObject[] targets = Selection.gameObjects;
            if (targets == null || targets.Length == 0)
                return;

            int done = 0;
            foreach (GameObject target in targets)
            {
                if (Fit(target))
                    done++;
            }

            if (done == 0)
            {
                Debug.LogWarning("[CapsuleColliderSetup] Nada ajustado — nenhum dos objetos selecionados tem " +
                                 "renderers para medir.");
            }
        }

        [MenuItem("Tools/The Delivery/Colisor - Ajustar Cápsula ao Modelo (Selecionados)", true)]
        private static bool Validate()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        private static bool Fit(GameObject target)
        {
            // Bounds de renderer em objeto desativado não valem nada — e o CreatureAtk vive
            // desativado. Liga, mede, devolve o estado.
            bool wasActive = target.activeSelf;
            if (!wasActive)
                target.SetActive(true);

            bool measured = TryGetBounds(target, out Bounds bounds);

            if (!wasActive)
                target.SetActive(false);

            if (!measured)
            {
                Debug.LogWarning($"[CapsuleColliderSetup] \"{target.name}\" não tem renderers com volume; " +
                                 "não dá para medir a cápsula.", target);
                return false;
            }

            Transform transform = target.transform;
            Vector3 lossy = transform.lossyScale;

            float scaleY = Mathf.Abs(lossy.y);
            float scaleXZ = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
            if (scaleY < 1e-6f || scaleXZ < 1e-6f)
            {
                Debug.LogWarning($"[CapsuleColliderSetup] \"{target.name}\" tem escala zerada em algum eixo; " +
                                 "a conversão para unidades locais é impossível.", target);
                return false;
            }

            // A Unity mede o raio pelo MAIOR entre X e Z e a altura por Y (na direção Y).
            // Com escala não-uniforme os dois divisores discordam, e a cápsula sai torta —
            // não é um erro deste comando, é como o CapsuleCollider funciona.
            if (!Mathf.Approximately(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z)) ||
                !Mathf.Approximately(Mathf.Abs(lossy.x), scaleY))
            {
                Debug.LogWarning($"[CapsuleColliderSetup] \"{target.name}\" está com escala NÃO-UNIFORME " +
                                 $"({lossy.x:0.###}, {lossy.y:0.###}, {lossy.z:0.###}). A cápsula vai ficar " +
                                 "aproximada — um CapsuleCollider não representa escala não-uniforme.", target);
            }

            var capsule = target.GetComponent<CapsuleCollider>();
            if (capsule == null)
                capsule = Undo.AddComponent<CapsuleCollider>(target);
            else
                Undo.RecordObject(capsule, "Ajustar Cápsula ao Modelo");

            float worldHeight = bounds.size.y;
            float worldRadius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f * RadiusFactor;

            // A cápsula não pode ser mais fina do que alta é possível: com raio maior que
            // metade da altura ela vira uma esfera, e a Unity ignora o excedente calada.
            worldRadius = Mathf.Min(worldRadius, worldHeight * 0.5f);

            capsule.direction = 1; // Y
            capsule.height = worldHeight / scaleY;
            capsule.radius = worldRadius / scaleXZ;

            // O centro sai dos BOUNDS convertidos para o espaço local — é o que corrige o
            // pivô nos pés. InverseTransformPoint já desfaz posição, rotação E escala.
            capsule.center = transform.InverseTransformPoint(bounds.center);

            EditorUtility.SetDirty(capsule);

            Debug.Log(
                $"[CapsuleColliderSetup] \"{target.name}\": cápsula de {worldHeight:0.##} m de altura e " +
                $"{worldRadius:0.##} m de raio.\n" +
                $"Escala do objeto {lossy.y:0.###}x, então em unidades locais ficou " +
                $"Height {capsule.height:0.#####}, Radius {capsule.radius:0.#####}, " +
                $"Center ({capsule.center.x:0.#####}, {capsule.center.y:0.#####}, {capsule.center.z:0.#####}).",
                target);

            return true;
        }

        /// <summary>União dos bounds dos renderers: o tamanho REAL do modelo na cena, escala incluída.</summary>
        private static bool TryGetBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y > 0.0001f;
        }
    }
}
