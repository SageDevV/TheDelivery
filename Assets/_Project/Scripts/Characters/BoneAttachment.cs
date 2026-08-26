using UnityEngine;

namespace TheDelivery.Characters
{
    /// <summary>
    /// Gruda este objeto num BONE de um personagem animado — o caso da xícara que precisa
    /// subir junto com a mão do idoso enquanto ele toma café. Todo frame, depois que o
    /// Animator já escreveu a pose, copia posição e rotação do bone com um offset fixo.
    ///
    /// POR QUE NÃO SIMPLESMENTE ARRASTAR O OBJETO PRA DENTRO DO BONE NA HIERARQUIA:
    /// funciona, mas o objeto passa a herdar a ESCALA do bone. Neste projeto isso é fatal —
    /// os FBX vêm em escalas malucas (a Xicara está na cena com escala 137), e a xícara
    /// pendurada no rig sairia gigante, minúscula ou torta, dependendo do bone. Seguindo por
    /// script o objeto fica FORA da hierarquia do rig: a escala é só dele, e reimportar o
    /// FBX não desfaz nada. O custo é um LateUpdate por objeto preso.
    ///
    /// Uso: rode <c>Tools ▸ The Delivery ▸ Anim - Prender à Mão (Objeto Selecionado)</c> com
    /// o objeto selecionado — o comando acha o bone e captura o offset da pose atual.
    ///
    /// AJUSTE FINO do encaixe (a xícara atravessando a mão, o cabo virado pro lado errado):
    /// mexa em <see cref="positionOffset"/>/<see cref="rotationOffset"/> e veja ao vivo no
    /// Scene view. Se preferir posicionar arrastando: desmarque
    /// <see cref="previewInEditor"/>, ponha a xícara onde quer, e use o menu de contexto do
    /// componente ▸ "Capturar offset da pose atual". Depois marque de volta.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BoneAttachment : MonoBehaviour
    {
        [Header("Alvo")]
        [Tooltip("O bone que comanda este objeto — para a xícara, o bone da MÃO (num rig Mixamo, 'mixamorig:RightHand'). Arraste da Hierarchy, de dentro do esqueleto do personagem.")]
        [SerializeField] private Transform bone;

        [Header("Encaixe")]
        [Tooltip("Deslocamento em relação ao bone, em METROS e no espaço de rotação dele. É o que tira a xícara de dentro da mão e a põe na palma.")]
        [SerializeField] private Vector3 positionOffset;
        [Tooltip("Giro em relação ao bone, em graus. É o que endireita a xícara (cabo pro lado certo, boca pra cima).")]
        [SerializeField] private Vector3 rotationOffset;

        [Header("O que copiar")]
        [Tooltip("Desligue se você quer que o objeto só ACOMPANHE a mão sem girar com ela — raro, mas útil pra coisas que precisam ficar sempre na vertical.")]
        [SerializeField] private bool followRotation = true;

        [Header("Editor")]
        [Tooltip("Mostra o encaixe no Scene view sem dar Play. DESMARQUE para conseguir arrastar o objeto à mão — com isto ligado ele volta pro bone no mesmo instante.")]
        [SerializeField] private bool previewInEditor = true;

        /// <summary>
        /// O bone que este objeto está seguindo. Trocar em runtime passa o objeto de uma mão
        /// pra outra; para SOLTAR (largar a xícara na mesa), desligue o componente com
        /// <c>enabled = false</c> — o objeto fica exatamente onde estava no último frame.
        /// </summary>
        public Transform Bone
        {
            get => bone;
            set => bone = value;
        }

        /// <summary>
        /// LateUpdate, não Update: o Animator escreve a pose do esqueleto durante o Update,
        /// então ler o bone antes disso pegaria a pose do frame ANTERIOR — a xícara ficaria
        /// um frame atrasada em relação à mão, e o atraso aparece justamente nos trechos
        /// rápidos do movimento, que é onde ele mais denuncia.
        /// </summary>
        private void LateUpdate()
        {
            if (bone == null)
                return;

            if (!Application.isPlaying && !previewInEditor)
                return;

            if (followRotation)
                transform.rotation = bone.rotation * Quaternion.Euler(rotationOffset);

            // Offset girado pelo bone mas NÃO escalado por ele (repare: bone.rotation *, e
            // não bone.TransformPoint). Se a escala do rig entrasse aqui, o offset em metros
            // deixaria de ser em metros e um bone com escala 137 jogaria a xícara pra fora
            // do prédio.
            transform.position = bone.position + bone.rotation * positionOffset;
        }

        /// <summary>
        /// Grava, como offset, a diferença entre onde o objeto está AGORA e o bone. É o
        /// caminho "posicionei arrastando, agora memoriza isso": desmarque
        /// <see cref="previewInEditor"/>, ajuste a xícara na mão, e chame isto.
        /// </summary>
        [ContextMenu("Capturar offset da pose atual")]
        public void CaptureOffset()
        {
            if (bone == null)
            {
                Debug.LogWarning("[BoneAttachment] Sem bone atribuído — não há de que capturar o offset.", this);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Capturar offset do encaixe");
#endif

            Quaternion inverse = Quaternion.Inverse(bone.rotation);
            positionOffset = inverse * (transform.position - bone.position);
            rotationOffset = (inverse * transform.rotation).eulerAngles;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        /// <summary>Linha do bone até o objeto, pra enxergar de onde o encaixe está pendurado.</summary>
        private void OnDrawGizmosSelected()
        {
            if (bone == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(bone.position, transform.position);
            Gizmos.DrawWireSphere(bone.position, 0.01f);
        }
#endif
    }
}
