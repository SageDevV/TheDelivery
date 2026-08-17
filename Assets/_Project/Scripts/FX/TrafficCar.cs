using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Carro de TRÁFEGO de fundo (a rua vista da Cafeteria): anda sempre PARA A FRENTE em
    /// linha reta e, ao percorrer <see cref="travelDistance"/> metros, ESPERA um tempo
    /// (aleatório entre <see cref="respawnDelayMin"/> e <see cref="respawnDelayMax"/>) e só
    /// então VOLTA ao ponto de origem para fazer tudo de novo. Esse intervalo é o que vende
    /// o trânsito: sem ele os carros viram um carrossel mecânico, com ele a rua "respira".
    ///
    /// Movimento SCRIPTADO (transform.Translate), como o <see cref="PoliceCarArrival"/> —
    /// NÃO usa física nem NavMesh: é cenário vivo, não um veículo dirigível. Se o prefab do
    /// carro tiver Collider, deixe-o em uma layer que não empurre o player (ou remova o
    /// Collider): um transform que atravessa um Rigidbody empurra de forma esquisita.
    ///
    /// Naturalidade (é o que separa "objeto deslizando" de "carro passando"):
    /// - cada volta sorteia uma velocidade nova entre <see cref="speedMin"/> e
    ///   <see cref="speedMax"/> — dois carros nunca andam idênticos;
    /// - <see cref="accelerationTime"/> faz o carro SAIR acelerando em vez de já nascer na
    ///   velocidade final;
    /// - <see cref="startDelay"/> escalona a entrada de cada carro (o setup de Editor
    ///   preenche isso sozinho na seleção) para eles não largarem todos juntos;
    /// - as rodas (<see cref="wheels"/>) giram na medida exata dos metros andados, então
    ///   aceleram junto com o carro e nunca patinam.
    ///
    /// Setup: coloque este script na RAIZ do carro, aponte-o para onde ele deve ir
    /// (<see cref="forwardAxis"/> resolve modelos cuja "frente" não é o +Z) e posicione o
    /// carro no INÍCIO do percurso — a pose inicial VIRA o ponto de origem do respawn.
    /// </summary>
    public sealed class TrafficCar : MonoBehaviour
    {
        /// <summary>Qual eixo LOCAL do modelo aponta para a frente do carro.</summary>
        public enum DriveAxis
        {
            /// <summary>+Z (padrão da Unity).</summary>
            Forward,
            /// <summary>-Z (modelo importado "de costas").</summary>
            Back,
            /// <summary>+X.</summary>
            Right,
            /// <summary>-X.</summary>
            Left
        }

        [Header("Direção")]
        [Tooltip("Eixo LOCAL que aponta para a FRENTE do modelo. Modelos importados (FBX) muitas vezes não têm a frente no +Z — se o carro andar de ré ou de lado, troque aqui em vez de girar o objeto na cena.")]
        [SerializeField] private DriveAxis forwardAxis = DriveAxis.Forward;

        [Header("Velocidade")]
        [Tooltip("Velocidade MÍNIMA de cruzeiro (m/s). ~8 m/s ≈ 30 km/h (rua de bairro).")]
        [SerializeField] private float speedMin = 7f;
        [Tooltip("Velocidade MÁXIMA de cruzeiro (m/s). A cada volta o carro sorteia um valor entre a mínima e a máxima — é o que impede o tráfego de parecer um carrossel.")]
        [SerializeField] private float speedMax = 11f;
        [Tooltip("Segundos para sair do zero até a velocidade de cruzeiro no começo de cada volta. 0 = parte já na velocidade final (mais 'robótico').")]
        [SerializeField] private float accelerationTime = 1.5f;

        [Header("Trajeto")]
        [Tooltip("Quantos METROS o carro percorre antes de sumir e voltar para a origem. Meça a rua no Scene view (o gizmo amarelo mostra o percurso quando este objeto está selecionado) e deixe o fim FORA do campo de visão do player.")]
        [SerializeField] private float travelDistance = 60f;

        [Header("Trânsito (delay antes do respawn)")]
        [Tooltip("Tempo MÍNIMO (s) de espera no FIM do percurso antes de voltar para a origem.")]
        [SerializeField] private float respawnDelayMin = 3f;
        [Tooltip("Tempo MÁXIMO (s) de espera no fim do percurso. O sorteio entre mínimo e máximo é o que espaça os carros de forma irregular, como trânsito de verdade.")]
        [SerializeField] private float respawnDelayMax = 9f;
        [Tooltip("Espera (s) ANTES da primeira largada. Serve para escalonar vários carros e não largarem todos no mesmo frame — o setup de Editor preenche isso sozinho na seleção.")]
        [SerializeField] private float startDelay;
        [Tooltip("Some com o carro (desliga os Renderers) enquanto ele espera no fim do percurso. Ligado é o seguro: se o fim do trajeto estiver visível, um carro parado ali entregaria o truque.")]
        [SerializeField] private bool hideWhileWaiting = true;

        [Header("Rodas")]
        [Tooltip("Os objetos VISUAIS das rodas (ex.: FrontLeftWheels, RearRightWheels). Giram na medida exata do avanço do carro, sem patinar. Vazio = ninguém gira. O setup de Editor preenche isso sozinho.")]
        [SerializeField] private Transform[] wheels;
        [Tooltip("Raio da roda em metros — é ele que converte metros andados em graus girados. Deixe 0 para MEDIR sozinho pelo tamanho da malha da roda no Start (funciona para qualquer carro importado).")]
        [SerializeField] private float wheelRadius;

        // Pose de origem: capturada no Start, é para onde o respawn devolve o carro.
        private Vector3 originPosition;
        private Quaternion originRotation;

        // Renderers que ESTE script apaga/acende na espera. Só os que já estavam visíveis no
        // Start — assim não "acendemos" um renderer que o artista deixou desligado de propósito.
        private Renderer[] bodyRenderers;

        // Raio efetivo das rodas: o wheelRadius do Inspector ou, se ele for 0, o medido na
        // malha no Start. Guardado à parte para não sobrescrever o campo serializado.
        private float effectiveWheelRadius;

        private float traveled;      // metros percorridos nesta volta
        private float cruiseSpeed;   // velocidade sorteada para esta volta
        private float passTime;      // segundos dirigindo nesta volta (para a aceleração)
        private float waitTimer;     // segundos restantes de espera (largada ou respawn)
        private bool driving;

        /// <summary>True enquanto o carro está andando (false enquanto espera para largar/renascer).</summary>
        public bool IsDriving => driving;

        /// <summary>
        /// Direção de avanço em espaço de MUNDO, já resolvendo o <see cref="forwardAxis"/>
        /// (a frente do modelo) com a rotação atual do objeto.
        /// </summary>
        public Vector3 DriveDirection => transform.TransformDirection(LocalDriveAxis());

        private void Start()
        {
            originPosition = transform.position;
            originRotation = transform.rotation;

            bodyRenderers = GetComponentsInChildren<Renderer>(false);
            effectiveWheelRadius = ResolveWheelRadius();

            // A primeira largada também espera: é o startDelay que escalona os carros.
            BeginWait(Mathf.Max(0f, startDelay));
        }

        private void Update()
        {
            if (driving)
                DriveStep();
            else
                WaitStep();
        }

        // --- API pública ---------------------------------------------------

        /// <summary>
        /// Devolve o carro AGORA para o ponto de origem e larga na hora (sem esperar o delay).
        /// Para um Director "resetar" a rua entre atos, ou para testar o percurso no play.
        /// </summary>
        public void ResetToOrigin()
        {
            transform.SetPositionAndRotation(originPosition, originRotation);
            traveled = 0f;
            passTime = 0f;
            cruiseSpeed = Random.Range(Mathf.Min(speedMin, speedMax), Mathf.Max(speedMin, speedMax));
            SetVisible(true);
            driving = true;
        }

        /// <summary>
        /// Interrompe a volta atual e manda o carro esperar <paramref name="seconds"/> antes de
        /// renascer na origem. Útil para um Director SEGURAR o tráfego numa cena de tensão
        /// (rua vazia) sem desligar o componente.
        /// </summary>
        public void HoldFor(float seconds) => BeginWait(seconds);

        // --- Interno -------------------------------------------------------

        /// <summary>
        /// Um passo de direção: acelera até a velocidade de cruzeiro, anda para a frente e,
        /// ao completar <see cref="travelDistance"/>, entra na espera do respawn.
        /// </summary>
        private void DriveStep()
        {
            passTime += Time.deltaTime;

            // Rampa de saída: 0 -> cruzeiro em accelerationTime segundos (SmoothStep evita o
            // "solavanco" de uma rampa linear no instante em que ela termina).
            float speed = accelerationTime > 0f
                ? Mathf.SmoothStep(0f, cruiseSpeed, Mathf.Clamp01(passTime / accelerationTime))
                : cruiseSpeed;

            float step = speed * Time.deltaTime;
            transform.Translate(LocalDriveAxis() * step, Space.Self);
            traveled += step;

            SpinWheels(step);

            if (traveled >= travelDistance)
            {
                // Chegou ao fim: some e aguarda o intervalo do trânsito antes de renascer.
                BeginWait(Random.Range(Mathf.Min(respawnDelayMin, respawnDelayMax),
                                       Mathf.Max(respawnDelayMin, respawnDelayMax)));
            }
        }

        /// <summary>
        /// Gira as rodas na medida EXATA do avanço do carro: os graus vêm dos metros
        /// percorridos (<paramref name="step"/> ÷ raio), não de uma velocidade solta — assim a
        /// roda nunca patina nem "arrasta", inclusive durante a rampa de aceleração.
        ///
        /// O giro acontece em torno do eixo de MUNDO perpendicular ao trajeto (a lateral do
        /// carro), não do eixo local de cada roda: modelos importados costumam ter as rodas do
        /// lado direito ESPELHADAS (giradas 180° no Y), e girar pelo eixo local faria um lado
        /// rodar para trás. Pelo mundo, os quatro pneus rodam juntos para a frente.
        /// </summary>
        private void SpinWheels(float step)
        {
            if (wheels == null || wheels.Length == 0 || effectiveWheelRadius <= 0f)
                return;

            // Arco -> ângulo: um pneu de raio r que anda 'step' metros gira step/r radianos.
            float degrees = step / effectiveWheelRadius * Mathf.Rad2Deg;
            Vector3 spinAxis = Vector3.Cross(Vector3.up, DriveDirection);

            if (spinAxis.sqrMagnitude < 0.0001f)
                return; // trajeto vertical (não deveria acontecer): sem eixo de giro válido

            foreach (Transform wheel in wheels)
            {
                if (wheel != null)
                    wheel.Rotate(spinAxis, degrees, Space.World);
            }
        }

        /// <summary>
        /// Raio usado no giro: o do Inspector quando &gt; 0, senão MEDIDO na malha da primeira
        /// roda (meia altura do bounding box do Renderer, já com a escala do objeto). Medir
        /// evita ter que descobrir na mão o raio de cada carro importado.
        /// </summary>
        private float ResolveWheelRadius()
        {
            if (wheelRadius > 0f)
                return wheelRadius;

            if (wheels == null)
                return 0f;

            foreach (Transform wheel in wheels)
            {
                if (wheel == null)
                    continue;

                Renderer wheelRenderer = wheel.GetComponentInChildren<Renderer>();
                if (wheelRenderer != null && wheelRenderer.bounds.extents.y > 0f)
                    return wheelRenderer.bounds.extents.y;
            }

            Debug.LogWarning("[TrafficCar] Não consegui medir o raio das rodas (nenhum Renderer nelas). " +
                             "Preencha o Wheel Radius no Inspector para os pneus girarem.", this);
            return 0f;
        }

        /// <summary>Conta a espera; ao zerar, devolve o carro à origem e larga.</summary>
        private void WaitStep()
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
                ResetToOrigin();
        }

        /// <summary>Para o carro, esconde (se configurado) e arma o cronômetro da espera.</summary>
        private void BeginWait(float seconds)
        {
            driving = false;
            waitTimer = Mathf.Max(0f, seconds);
            SetVisible(false);
        }

        /// <summary>
        /// Liga/desliga os Renderers do carro. No-op quando <see cref="hideWhileWaiting"/>
        /// está desmarcado (aí o carro fica visível parado no fim do percurso).
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (!hideWhileWaiting || bodyRenderers == null)
                return;

            foreach (Renderer bodyRenderer in bodyRenderers)
            {
                if (bodyRenderer != null)
                    bodyRenderer.enabled = visible;
            }
        }

        /// <summary>O <see cref="forwardAxis"/> como vetor no espaço LOCAL do carro.</summary>
        private Vector3 LocalDriveAxis()
        {
            switch (forwardAxis)
            {
                case DriveAxis.Back: return Vector3.back;
                case DriveAxis.Right: return Vector3.right;
                case DriveAxis.Left: return Vector3.left;
                default: return Vector3.forward;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Desenha o percurso no Scene view (origem -> fim) para dar pra medir a rua e
        /// escolher o <see cref="travelDistance"/> sem chutar. Fora do play a origem é a pose
        /// atual do objeto; rodando, é a pose capturada no Start.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 start = Application.isPlaying ? originPosition : transform.position;
            Vector3 end = start + DriveDirection * travelDistance;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 0.5f);
            Gizmos.DrawWireSphere(end, 0.5f);
        }
#endif
    }
}
