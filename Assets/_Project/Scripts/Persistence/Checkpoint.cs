using UnityEngine;

namespace TheDelivery.Persistence
{
    /// <summary>
    /// CHECKPOINT DE SAVE AUTOMÁTICO no meio de um ato. Um trigger: quando o jogador entra,
    /// salva o estado atual (cena, ato, beat do diretor, estamina, itens, flags) e — se
    /// <see cref="savePlayerPose"/> — a posição/rotação, para o "Carregar Jogo" voltar
    /// exatamente aqui.
    ///
    /// Use em momentos de EXPLORAÇÃO LIVRE (o jogador anda solto). Num trecho roteirizado
    /// (cutscene, câmera dirigida) deixe savePlayerPose desligado: quem posiciona o jogador ao
    /// retomar é o próprio beat.
    ///
    /// Setup: um GameObject com Collider marcado como Is Trigger + este componente. O início
    /// de cada ato já é salvo sozinho pelo GameManager — checkpoint é para o MEIO do ato.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [Tooltip("Identificador estável (vai no save). Ex.: \"act4.bathroom\".")]
        [SerializeField] private string checkpointId = "checkpoint";
        [Tooltip("Salva a posição/rotação do jogador (exploração livre). Desligue em trechos roteirizados.")]
        [SerializeField] private bool savePlayerPose = true;
        [Tooltip("Salva só na primeira vez que o jogador passa (por sessão).")]
        [SerializeField] private bool once = true;
        [Tooltip("Flag de história marcada ao passar (opcional). Ex.: \"act2.reachedElevator\".")]
        [SerializeField] private string setFlag = "";

        private const string PlayerTag = "Player";
        private bool used;

        public string CheckpointId => checkpointId;

        private void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (once && used)
                return;
            if (!other.CompareTag(PlayerTag) && other.GetComponentInParent<CharacterController>() == null)
                return;

            used = true;
            if (!string.IsNullOrEmpty(setFlag))
                SaveManager.SetFlag(setFlag);
            SaveManager.SaveCheckpoint(checkpointId, savePlayerPose);
        }

        /// <summary>Salva por código (ex.: um diretor no fim de um beat importante).</summary>
        public void SaveNow() => SaveManager.SaveCheckpoint(checkpointId, savePlayerPose);

        private void OnDrawGizmos()
        {
            var c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (c is BoxCollider b) Gizmos.DrawCube(b.center, b.size);
            else if (c is SphereCollider s) Gizmos.DrawSphere(s.center, s.radius);
        }
    }
}
