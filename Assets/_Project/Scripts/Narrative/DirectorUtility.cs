using UnityEngine;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Helpers SEM ESTADO compartilhados pelos diretores de ato
    /// (<see cref="Act1Director"/> … <see cref="PesadeloDirector"/>).
    ///
    /// Antes cada diretor carregava a própria cópia destes métodos — idênticas, com
    /// comentários do tipo "espelha o do Act2Director". Uma correção feita num ato não
    /// chegava aos outros. Aqui fica a versão única; os diretores importam com
    /// <c>using static TheDelivery.Narrative.DirectorUtility;</c> e chamam pelo nome
    /// curto (<c>ShowThought(x)</c>), então os beats continuam lendo igual.
    ///
    /// O que DEPENDE de campo do diretor (o player, o groundMask, o standUpPrompt) recebe
    /// esse campo por parâmetro: este tipo não guarda nada e não conhece nenhum ato.
    /// </summary>
    public static class DirectorUtility
    {
        // Folga de 1 m acima do alvo para o raycast de chão, e alcance total de 4 m:
        // cobre o ponto autorado um pouco acima ou abaixo do piso sem achar o teto.
        private const float GroundProbeHeight = 1f;
        private const float GroundProbeDistance = 4f;

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        public static void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>Dispara um diálogo via DialogueSystem, se ambos existirem.</summary>
        public static void ShowDialogue(DialogueData dialogue)
        {
            if (dialogue != null && DialogueSystem.Instance != null)
                DialogueSystem.Instance.Show(dialogue);
        }

        /// <summary>Distância no plano XZ — a altura não conta para "chegou".</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// True se <paramref name="position"/> está dentro do raio (XZ) de
        /// <paramref name="center"/>. A altura é ignorada de propósito: o ponto de
        /// referência fica no chão e a posição testada costuma ser a dos pés ou a dos olhos.
        /// </summary>
        public static bool IsWithinRadiusXZ(Vector3 position, Vector3 center, float radius)
        {
            float dx = position.x - center.x;
            float dz = position.z - center.z;
            return (dx * dx + dz * dz) <= radius * radius;
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <paramref name="groundMask"/>), partindo de 1 m acima, evitando que um
        /// personagem termine flutuando e "caia" quando o CharacterController religa.
        /// Se nada for atingido, devolve a posição original.
        /// </summary>
        public static Vector3 SnapToGround(Vector3 position, LayerMask groundMask)
        {
            Vector3 origin = position + Vector3.up * GroundProbeHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundProbeDistance, groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y, position.z);
            return position;
        }

        /// <summary>Gira <paramref name="who"/> no plano XZ para encarar <paramref name="worldTarget"/>.</summary>
        public static void FaceTowards(Transform who, Vector3 worldTarget)
        {
            Vector3 dir = worldTarget - who.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                who.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>
        /// Garante o estado LIVRE do player: pode andar e olhar, sem trava de olhar
        /// herdada, CharacterController habilitado, interação ligada e o prompt de
        /// levantar (se houver) escondido. Os beats em que o player deve estar livre
        /// chamam isto no início — assim pular direto para um beat (debug) ou chegar
        /// de outra cena nunca deixa o player travado.
        /// </summary>
        public static void ReleasePlayer(
            PlayerController playerController,
            CharacterController characterController,
            PlayerInteraction playerInteraction,
            GameObject standUpPrompt)
        {
            if (characterController != null && !characterController.enabled)
                characterController.enabled = true;

            playerController.CanLookOverride = false;
            playerController.CanMove = true;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);
        }

        /// <summary>
        /// Resolve e configura o AudioSource 2D dos sons narrativos de um diretor: usa o
        /// atribuído, senão o do próprio GameObject, senão cria um. Sempre 2D, sem
        /// Play On Awake e sem loop — o clip pode vir importado como 3D, mas som
        /// narrativo tem que ser audível de qualquer lugar.
        /// </summary>
        public static AudioSource EnsureNarrativeAudioSource(MonoBehaviour owner, AudioSource source)
        {
            if (source == null)
                source = owner.GetComponent<AudioSource>();
            if (source == null)
            {
                source = owner.gameObject.AddComponent<AudioSource>();
                Debug.Log($"[{owner.GetType().Name}] AudioSource ausente; criado automaticamente (2D, sem Play On Awake).", owner);
            }

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }
    }
}
