using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// TOQUE DE RECOLHER genérico: tira um figurante (pedestre, objeto de cena) da rua conforme o
    /// <see cref="NightfallController"/> anoitece — a mesma regra do tráfego
    /// (<see cref="StreetTrafficCar"/>): à noite a rua é vazia.
    ///
    /// <list type="bullet">
    /// <item>Depois de <see cref="leaveAt"/>: sai na primeira oportunidade DISCRETA — quando o próprio
    /// figurante estiver escondido (todos os renderers apagados, como o <c>AmbientWalker</c> faz entre
    /// uma caminhada e outra). Assim ninguém some na frente da câmera.</item>
    /// <item>Em <see cref="vanishAt"/> (noite fechada): sai de qualquer jeito.</item>
    /// </list>
    /// "Sair" = desativar o GameObject alvo (por padrão, este).
    /// </summary>
    public sealed class NightfallCurfew : MonoBehaviour
    {
        [SerializeField] private NightfallController nightfall;
        [Tooltip("O que sai da rua. Vazio = este GameObject.")]
        [SerializeField] private GameObject target;
        [Tooltip("Progresso do anoitecer a partir do qual o figurante sai quando estiver escondido.")]
        [Range(0f, 1f)] [SerializeField] private float leaveAt = 0.7f;
        [Tooltip("Progresso em que sai de qualquer jeito (noite fechada).")]
        [Range(0f, 1f)] [SerializeField] private float vanishAt = 0.97f;

        private Renderer[] renderers;

        private void Start()
        {
            if (target == null)
                target = gameObject;
            if (nightfall == null)
                nightfall = FindAnyObjectByType<NightfallController>();
            renderers = target.GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
            if (nightfall == null || target == null || !target.activeSelf)
                return;
            float p = nightfall.Progress;
            if (p >= vanishAt || (p >= leaveAt && IsHidden()))
                target.SetActive(false);
        }

        private bool IsHidden()
        {
            foreach (Renderer r in renderers)
                if (r != null && r.enabled)
                    return false;
            return true;
        }
    }
}
