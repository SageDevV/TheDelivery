using System.Collections.Generic;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// AJUSTA A PEGADA COM OS OLHOS. Põe a criatura do beat final na cena, toca o beat
    /// inteiro dentro do editor e mostra o resultado por DUAS CÂMERAS ao mesmo tempo: a da
    /// Clear (o que o jogador vai ver) e uma de fora, orbitando o par.
    ///
    /// POR QUE DUAS: as perguntas são diferentes e uma câmera só responde uma delas. De
    /// fora dá para ver se o corpo está pendurado na mão ou boiando ao lado dela, se ele
    /// atravessa o peito da criatura, a que altura do chão está. Pelos olhos da Clear dá
    /// para ver a única coisa que o jogador vai receber: se a criatura está enquadrada, se
    /// o braço entra em cena, se o rosto dela aparece. Um encaixe pode estar perfeito de
    /// fora e mostrar um ombro ocupando a tela inteira por dentro.
    ///
    /// O ENCAIXE é ajustado aqui porque só faz sentido contra a pose do clipe, e ele vive
    /// sempre no espaço do OSSO da mão: a mão pode fechar, erguer e girar o punho que a Clear
    /// vai junto, como a xícara do Ato 1 vai junto com a mão do idoso.
    ///
    /// DE UM ENCAIXE PARA VÁRIOS. Um offset só já basta para a Clear ficar na mão o clipe
    /// inteiro. Os ESTADOS entram por cima disso, para o que o osso não conta: o contato muda
    /// ao longo do gesto — no bote a mão fecha no pescoço e a cabeça entra mais na palma;
    /// erguida no alto, o corpo pende e a cabeça desce em relação ao punho. A janela lê as
    /// fases do clipe (o bote, cada levantamento), cria um estado em cada uma e deixa você
    /// posicionar o encaixe de cada estado com a alça, vendo os outros como fantasmas.
    ///
    /// (Não confunda com a lista que este beat já teve: aquela era em eixos de MUNDO e
    /// existia para remendar um encaixe que escorregava da mão a cada giro de punho. Estes
    /// são offsets do OSSO — o pior que um estado desafinado faz é deslocar a Clear alguns
    /// centímetros.)
    ///
    /// O clipe é amostrado pelo <see cref="AnimationMode"/>, o mesmo que a janela Animation
    /// usa: a pose é escrita nos transforms para você ver e DESFEITA ao sair do modo. Nada
    /// vira modificação de cena.
    /// </summary>
    public sealed class PesadeloGrabPreviewWindow : EditorWindow
    {
        private PesadeloDirector director;
        private SerializedObject directorSo;

        private GameObject grab;
        private AnimationClip clip;
        private Transform hand;
        private Transform head;

        private bool previewing;
        private float clipTime;
        private Vector2 scroll;

        // Estado da cena guardado ao entrar no preview. O AnimationMode desfaz a POSE, mas
        // não o SetActive nem o lugar em que a criatura foi plantada — isso é por nossa conta.
        private bool savedActive;
        private Vector3 savedPosition;
        private Quaternion savedRotation;

        // --- Câmeras do preview ---
        // Criadas com HideAndDontSave: elas existem só enquanto a janela está aberta e não
        // podem, em hipótese alguma, ser salvas dentro da cena.
        private Camera eyeCamera;
        private Camera orbitCamera;
        private float orbitYaw = 35f;
        private float orbitPitch = 12f;
        private float orbitDistance = 4.5f;
        private bool showEyeView = true;
        private bool showOrbitView = true;

        // --- Reprodução ---
        private bool playing;
        private double lastTick;
        private float beatTime;

        /// <summary>
        /// UM MOMENTO MARCANTE DO CLIPE, lido dele e não autorado: a mão chegando na Clear, o
        /// primeiro topo, o topo mais alto.
        ///
        /// O que fica guardado é a pose da MÃO, não a da Clear. Com o encaixe preso ao osso,
        /// mexer no offset move todos os fantasmas junto sem reamostrar o clipe — e reamostrar
        /// a cada repaint é o que tornaria a janela inutilizável.
        /// </summary>
        private struct HoldPhase
        {
            public string label;
            public float clipTime;
            public Vector3 handPosition;
            public Quaternion handRotation;
        }

        // --- Fases lidas do clipe, uma vez por preview ---
        private readonly List<HoldPhase> phases = new();
        private bool secondLiftFound;

        // --- Estados do encaixe ---
        // A pose da MÃO no quadro de cada estado, reamostrada só quando os tempos mudam. É o
        // que deixa os fantasmas seguirem o offset ao vivo sem tocar no AnimationMode.
        private readonly List<HoldPhase> keyPoses = new();
        private int selectedKey = -1;

        // --- Trajetória da queda, recalculada sob demanda ---
        private readonly List<Vector3> trajectory = new();
        private Vector3 releaseEye;
        // Os PÉS no quadro da soltura, como a ANIMAÇÃO os deixa — o releaseEye descontada a
        // altura do olho. Separado do fallStart porque os dois deixam de ser a mesma coisa
        // assim que há um marcador: é comparando um com o outro que se vê o salto.
        private Vector3 heldFeet;
        // De onde a queda parte DE FATO: o marcador, se houver, senão o heldFeet. Guardado
        // em vez de refeito porque quem o lê é o EyeAtBeatTime, a cada frame da reprodução.
        private Vector3 fallStart;
        private Vector3 landingFeet;
        // A velocidade da queda — constante do primeiro quadro ao pouso, resolvida da altura
        // e do prazo. Guardada porque a reprodução a lê a cada frame e recalculá-la lá
        // arriscaria os dois caminhos discordarem.
        private float releaseVerticalSpeed;
        // Quanto a queda livre levaria SEM teto. Só serve ao readout: é o número que explica
        // por que a queda mostrada é mais rápida do que a física pediria.
        private float freeFallSolved;
        private float flightDuration;
        private bool trajectoryValid;

        private const int TrajectorySamples = 32;
        private const float ViewHeight = 190f;

        [MenuItem("Tools/The Delivery/Pesadelo - Ajustar a Pegada")]
        private static void Open()
        {
            var window = GetWindow<PesadeloGrabPreviewWindow>("Pegada");
            window.minSize = new Vector2(420, 620);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Bind();

            // RECUPERAÇÃO DEPOIS DE UM RECOMPILE. Os campos da janela sobrevivem ao domain
            // reload (o EditorWindow os serializa), mas o AnimationMode NÃO — ele cai, e as
            // câmeras do preview morrem junto. Sem isto a janela acharia que ainda está
            // previsualizando, e a criatura ficaria ligada e plantada no meio do corredor.
            if (previewing && !AnimationMode.InAnimationMode())
                StopPreview();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            StopPreview();
        }

        // --- Ligação com a cena --------------------------------------------

        private void Bind()
        {
            director = null;
            directorSo = null;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                director = PesadeloGrabSetup.FindDirector(scene);
                if (director != null)
                    break;
            }

            if (director == null)
                return;

            directorSo = new SerializedObject(director);
            grab = directorSo.FindProperty("creatureGrabObject")?.objectReferenceValue as GameObject;
            clip = ResolveClip(grab);
        }

        private static AnimationClip ResolveClip(GameObject model)
        {
            if (model == null)
                return null;

            var animator = model.GetComponentInChildren<Animator>(includeInactive: true);
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null)
                return null;

            foreach (AnimationClip candidate in controller.animationClips)
                if (candidate != null)
                    return candidate;

            return null;
        }

        // --- Janela --------------------------------------------------------

        private void OnGUI()
        {
            if (director == null || grab == null || clip == null)
            {
                DrawNotReady();
                return;
            }

            directorSo.Update();

            DrawPreviewToggle();

            using (new EditorGUI.DisabledScope(!previewing))
            {
                DrawViews();

                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawTransport();
                EditorGUILayout.Space();
                DrawTimeline();
                EditorGUILayout.Space();
                DrawPhases();
                EditorGUILayout.Space();
                DrawAttachment();
                EditorGUILayout.Space();
                DrawDropFields();
                EditorGUILayout.Space();
                DrawReadout();
                EditorGUILayout.EndScrollView();
            }

            directorSo.ApplyModifiedProperties();
        }

        private void DrawNotReady()
        {
            EditorGUILayout.HelpBox(
                director == null
                    ? "Nenhum PesadeloDirector nas cenas abertas. Abra a cena Pesadelo."
                    : grab == null
                        ? "O campo \"Creature Grab Object\" do director está vazio."
                        : "O CreatureGrab não tem controller com clipe — sem clipe não há o que percorrer.",
                MessageType.Info);

            if (GUILayout.Button("Abrir a cena Pesadelo e preparar o beat"))
            {
                Scene scene = PesadeloGrabSetup.EnsureSceneOpen();
                if (scene.IsValid())
                {
                    EditorApplication.ExecuteMenuItem("Tools/The Delivery/Pesadelo - Beat Final (Pegada)");
                    Bind();
                }
            }

            if (GUILayout.Button("Procurar de novo"))
                Bind();
        }

        private void DrawPreviewToggle()
        {
            if (!previewing)
            {
                if (GUILayout.Button("Ligar pré-visualização", GUILayout.Height(28)))
                    StartPreview();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(1f, 0.75f, 0.75f);
                if (GUILayout.Button("Desligar", GUILayout.Height(22), GUILayout.Width(90)))
                    StopPreview();
                GUI.backgroundColor = Color.white;

                showEyeView = GUILayout.Toggle(showEyeView, "Visão da Clear", EditorStyles.miniButtonLeft);
                showOrbitView = GUILayout.Toggle(showOrbitView, "Terceira pessoa", EditorStyles.miniButtonRight);
            }
        }

        // --- As duas visões ------------------------------------------------

        /// <summary>
        /// Desenha as duas câmeras do preview dentro da janela.
        ///
        /// <see cref="Handles.DrawCamera"/> é o caminho certo aqui, e não um
        /// <c>Camera.Render()</c> para RenderTexture: ele é a API que o próprio editor usa
        /// para desenhar câmeras em rects de GUI, e por isso atravessa o URP sem precisar
        /// saber nada sobre o pipeline. Renderizar à mão exigiria conversar com o
        /// RenderPipeline diretamente, e essa conversa muda de assinatura a cada versão.
        /// </summary>
        private void DrawViews()
        {
            if (!previewing)
                return;

            int panes = (showEyeView ? 1 : 0) + (showOrbitView ? 1 : 0);
            if (panes == 0)
                return;

            UpdateCameras();

            float width = (position.width - 12f - (panes - 1) * 4f) / panes;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (showEyeView)
                    DrawCameraPane(eyeCamera, "Visão da Clear", width);

                if (showOrbitView)
                    DrawCameraPane(orbitCamera, "Terceira pessoa", width);
            }

            if (showOrbitView)
                DrawOrbitControls();
        }

        private void DrawCameraPane(Camera camera, string title, float width)
        {
            Rect rect = GUILayoutUtility.GetRect(width, ViewHeight, GUILayout.Width(width), GUILayout.Height(ViewHeight));

            if (camera != null && Event.current.type == EventType.Repaint)
                Handles.DrawCamera(rect, camera, DrawCameraMode.Textured);

            var label = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, 16f);
            EditorGUI.DropShadowLabel(label, title);
        }

        /// <summary>
        /// Os controles da câmera de fora. Ela ORBITA o ponto em que a Clear está — não a
        /// criatura — porque o que se está ajustando é a posição DELA; uma órbita presa na
        /// criatura tiraria a Clear de quadro justamente quando o braço a levasse para o
        /// alto, que é o momento que mais precisa ser visto.
        /// </summary>
        private void DrawOrbitControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Órbita", GUILayout.Width(45));
                orbitYaw = EditorGUILayout.Slider(orbitYaw, -180f, 180f);
                EditorGUILayout.LabelField("Altura", GUILayout.Width(45));
                orbitPitch = EditorGUILayout.Slider(orbitPitch, -40f, 70f);
                EditorGUILayout.LabelField("Zoom", GUILayout.Width(38));
                orbitDistance = EditorGUILayout.Slider(orbitDistance, 1f, 12f);
            }
        }

        /// <summary>Cria (uma vez) e reposiciona as duas câmeras para o instante atual do beat.</summary>
        private void UpdateCameras()
        {
            eyeCamera = EnsureCamera(eyeCamera, "PesadeloPreviewEye");
            orbitCamera = EnsureCamera(orbitCamera, "PesadeloPreviewOrbit");

            Vector3 eye = EyeAtBeatTime();
            Vector3 target = head != null ? head.position : grab.transform.position;

            Vector3 forward = target - eye;
            if (forward.sqrMagnitude < 0.0001f)
                forward = grab.transform.forward;

            // A câmera da Clear leva o TOMBO da queda junto: presa na mão não há inclinação
            // nenhuma (quem enquadra é a mira na criatura), e caindo ela tomba até a pose do
            // chão — que é o último frame do pesadelo, visto por dentro.
            Quaternion aimed = Quaternion.LookRotation(forward.normalized, Vector3.up);

            // E ela leva o GIRO: no começo do beat o olhar ainda está no eixo do corredor e
            // vai até a criatura em grabTurnDuration segundos, com a mesma SmoothStep do
            // director. É por esta interpolação que dá para julgar se o giro ficou leve.
            Quaternion look = Quaternion.Slerp(StandingLook(), aimed, TurnProgress()) * CurrentTilt();

            eyeCamera.transform.SetPositionAndRotation(eye, look);

            Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 pivot = Vector3.Lerp(eye, target, 0.35f);
            orbitCamera.transform.position = pivot - orbit * Vector3.forward * orbitDistance;
            orbitCamera.transform.rotation = orbit;
        }

        private Camera EnsureCamera(Camera camera, string name)
        {
            if (camera != null)
                return camera;

            // HideAndDontSave: a câmera não aparece na Hierarchy e NUNCA é gravada na cena —
            // é um objeto de ferramenta, e um objeto de ferramenta salvo no .unity é lixo que
            // alguém vai encontrar meses depois sem saber de onde veio.
            var host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.cameraType = CameraType.Preview;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 1f);
            return camera;
        }

        private void DestroyCameras()
        {
            if (eyeCamera != null)
                DestroyImmediate(eyeCamera.gameObject);
            if (orbitCamera != null)
                DestroyImmediate(orbitCamera.gameObject);

            eyeCamera = null;
            orbitCamera = null;
        }

        // --- Transporte e timeline -----------------------------------------

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "❚❚  Pausar" : "▶  Tocar o beat", GUILayout.Height(24)))
                    TogglePlay();

                if (GUILayout.Button("↺  Do início", GUILayout.Height(24), GUILayout.Width(100)))
                {
                    beatTime = 0f;
                    ApplyBeatTime();
                }
            }

            EditorGUILayout.LabelField(
                $"{beatTime:0.00} s de {BeatLength():0.00} s   —   {PhaseName()}",
                EditorStyles.miniLabel);
        }

        private void DrawTimeline()
        {
            SerializedProperty release = directorSo.FindProperty("grabReleaseNormalizedTime");

            EditorGUI.BeginChangeCheck();
            clipTime = EditorGUILayout.Slider("Quadro do clipe", clipTime, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                playing = false;
                beatTime = TurnDuration() + clipTime * clip.length;
                Sample(clipTime);
            }

            EditorGUILayout.LabelField(
                $"quadro {Mathf.RoundToInt(clipTime * clip.length * clip.frameRate)} de " +
                $"{Mathf.RoundToInt(clip.length * clip.frameRate)}   ({clipTime * clip.length:0.00} s)",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ quadro"))
                    StepFrame(-1);
                if (GUILayout.Button("quadro ▶"))
                    StepFrame(1);
                if (release != null && GUILayout.Button("Ir para a soltura"))
                    GoTo(release.floatValue);
            }

            // A guarda não é contra o zero: num clipe de 280 quadros o quadro 1 é 0,0036 e
            // passaria por qualquer teste contra zero, produzindo o mesmo desastre — a Clear
            // largada antes de a criatura tê-la puxado. O que define "cedo demais" é o tempo
            // da puxada, convertido para fração do clipe. Mesma conta do director.
            float floor = ReleaseFloor();

            using (new EditorGUI.DisabledScope(clipTime <= floor))
            {
                if (release != null && GUILayout.Button("Marcar a soltura neste quadro", GUILayout.Height(20)))
                {
                    release.floatValue = clipTime;
                    directorSo.ApplyModifiedProperties();
                    MarkDirty();
                    RebuildTrajectory();
                }
            }

            if (release != null && release.floatValue <= floor)
            {
                EditorGUILayout.HelpBox(
                    $"A soltura está em {release.floatValue:0.###}, antes de a Clear terminar de chegar à mão (a " +
                    $"puxada ocupa até {floor:0.###} do clipe). Em play o beat corrige para 0.8 e avisa, mas marque " +
                    "o quadro certo: percorra até a mão abrir e clique no botão acima.",
                    MessageType.Warning);

                // O mesmo 0.8 que o director usa quando corrige sozinho, mas GRAVADO — para
                // a cena parar de depender da correção em runtime enquanto o quadro certo não
                // é marcado à mão.
                if (GUILayout.Button("Usar 0.8 por enquanto", EditorStyles.miniButton))
                {
                    release.floatValue = 0.8f;
                    directorSo.ApplyModifiedProperties();
                    MarkDirty();
                    GoTo(0.8f);
                    RebuildTrajectory();
                }
            }
        }

        // --- As fases do agarrão, lidas do clipe ----------------------------

        /// <summary>
        /// Os momentos marcantes da animação, para poder pular até cada um e ver a Clear ali.
        ///
        /// A CRIATURA ERGUE DUAS VEZES: ela pega, levanta, cede um pouco e levanta MAIS ALTO
        /// antes de largar. São esses os momentos que viram ESTADOS do encaixe — o botão de
        /// semear cria um em cada um deles —, e são eles que decidem onde a mão abre: uma
        /// soltura marcada antes do último topo larga a Clear no meio da subida.
        ///
        /// Os tempos são lidos do clipe, e não escritos à mão, porque tempo escrito à mão
        /// envelhece calado: trocado o clipe, os números continuam lá apontando para quadros
        /// que não existem mais.
        /// </summary>
        private void DrawPhases()
        {
            EditorGUILayout.LabelField("Fases do clipe", EditorStyles.boldLabel);

            if (phases.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "Ligue a pré-visualização para a janela ler a coreografia do clipe.",
                    EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (HoldPhase phase in phases)
                {
                    bool current = Mathf.Abs(clipTime - phase.clipTime) < 0.01f;
                    GUI.backgroundColor = current ? new Color(0.6f, 0.9f, 1f) : Color.white;

                    if (GUILayout.Button($"{phase.label}\n{phase.clipTime:0.00}", EditorStyles.miniButton,
                                         GUILayout.Height(30)))
                    {
                        GoTo(phase.clipTime);
                    }
                }

                GUI.backgroundColor = Color.white;
            }

            if (!secondLiftFound)
            {
                EditorGUILayout.HelpBox(
                    "A mão sobe UMA vez só neste clipe — não achei um segundo levantamento (um topo que cede pelo " +
                    "menos 5 cm e volta a subir). Se a criatura devia erguer a Clear mais uma vez, o movimento não " +
                    "está na animação: é o clipe do CreatureGrab que precisa mudar, não o beat. A Clear acompanha o " +
                    "que a mão fizer.",
                    MessageType.Info);
            }

            DrawSoundCue();

            // A SOLTURA TEM QUE VIR DEPOIS DO ÚLTIMO TOPO. Antes dele a mão ainda está
            // subindo, e largar ali é o que faz a queda começar no meio do gesto — o sintoma
            // que parece "a criatura arremessou a Clear".
            SerializedProperty release = directorSo.FindProperty("grabReleaseNormalizedTime");
            float top = phases[phases.Count - 1].clipTime;

            if (release != null && release.floatValue < top)
            {
                EditorGUILayout.HelpBox(
                    $"A soltura está em {release.floatValue:0.###}, ANTES do ponto mais alto ({top:0.##}): a Clear é " +
                    "largada no meio da subida, e o resto da animação acontece sem ela na mão.",
                    MessageType.Warning);

                if (GUILayout.Button($"Marcar a soltura no ponto mais alto ({top:0.##})", EditorStyles.miniButton))
                {
                    release.floatValue = top;
                    directorSo.ApplyModifiedProperties();
                    MarkDirty();
                    GoTo(top);
                    RebuildTrajectory();
                }
            }
        }

        /// <summary>
        /// O QUADRO DO SOM. Ele é um impacto — a mão fechando no pescoço —, então o lugar
        /// dele é o quadro em que a mão ENCOSTA nela, que é justamente a fase "a mão pega"
        /// que a janela já achou. Um clique para marcar, e o beat toca o som ali.
        ///
        /// Antes ele tocava quando a criatura APARECIA, e o defeito era audível: o baque
        /// acontecia antes de qualquer coisa se mexer.
        /// </summary>
        private void DrawSoundCue()
        {
            SerializedProperty cue = directorSo.FindProperty("grabSoundClipTime");
            SerializedProperty sound = directorSo.FindProperty("grabSound");
            if (cue == null)
                return;

            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Som do agarrão em {cue.floatValue:0.00}",
                                           EditorStyles.miniLabel, GUILayout.MinWidth(140));

                if (GUILayout.Button("Marcar o som neste quadro", EditorStyles.miniButton))
                {
                    cue.floatValue = clipTime;
                    directorSo.ApplyModifiedProperties();
                    MarkDirty();
                }

                // O atalho: a fase do bote é o lugar certo em quase todo clipe, e achá-la
                // percorrendo quadro a quadro é trabalho que a janela já fez.
                using (new EditorGUI.DisabledScope(phases.Count == 0))
                {
                    if (GUILayout.Button($"Usar \"{(phases.Count > 0 ? phases[0].label : "a mão pega")}\"",
                                         EditorStyles.miniButton))
                    {
                        cue.floatValue = phases[0].clipTime;
                        directorSo.ApplyModifiedProperties();
                        MarkDirty();
                        GoTo(phases[0].clipTime);
                    }
                }
            }

            if (sound != null && sound.objectReferenceValue == null)
            {
                EditorGUILayout.LabelField("Sem clipe de áudio no Grab Sound — não há o que tocar.",
                                           EditorStyles.miniLabel);
                return;
            }

            SerializedProperty release = directorSo.FindProperty("grabReleaseNormalizedTime");
            if (release != null && cue.floatValue >= release.floatValue)
            {
                EditorGUILayout.HelpBox(
                    $"O som está marcado em {cue.floatValue:0.##}, que é DEPOIS da soltura ({release.floatValue:0.##}): " +
                    "a essa altura a mão já abriu. Em play o beat toca o som na soltura para ele não sumir, mas o " +
                    "baque vai soar atrasado — marque-o no quadro em que a mão encosta nela.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// LÊ A COREOGRAFIA DO CLIPE em vez de pedir que alguém a descreva: amostra a pose do
        /// osso da mão quadro a quadro e marca o instante em que ela CHEGA na Clear, o
        /// primeiro topo e o topo MAIS ALTO.
        ///
        /// POR QUE DETECTAR E NÃO AUTORAR: este beat já teve uma lista de estados escrita à
        /// mão, e o problema dela não era só o encaixe — era que os tempos envelheciam.
        /// Trocado o clipe, os cinco números continuavam lá, apontando para quadros que não
        /// existiam mais, e nada avisava. Lidos do clipe, eles não têm como discordar dele.
        ///
        /// AMOSTRAR CUSTA: é um SampleAnimationClip por quadro, com o esqueleto inteiro sendo
        /// posado. Por isso roda UMA vez ao ligar o preview (e ao replantar a criatura, que
        /// muda as posições de mundo), e não a cada repaint.
        /// </summary>
        private void RebuildPhases()
        {
            phases.Clear();
            secondLiftFound = false;

            if (!previewing || hand == null || clip == null || clip.length <= 0.0001f)
                return;

            int samples = Mathf.Clamp(Mathf.RoundToInt(clip.length * clip.frameRate), 8, 240);
            var height = new float[samples + 1];
            var positions = new Vector3[samples + 1];
            var rotations = new Quaternion[samples + 1];

            // O instante do BOTE é o quadro em que a mão chega mais perto de onde a cabeça da
            // Clear está de pé. É a definição honesta de "a criatura pega": o resto do clipe
            // (o braço voltando, a mão abrindo) acontece longe dela.
            Vector3 standing = StandingEye();
            int grabIndex = 0;
            float closest = float.MaxValue;

            for (int i = 0; i <= samples; i++)
            {
                SampleSeconds(clip.length * i / samples);

                positions[i] = hand.position;
                rotations[i] = hand.rotation;
                height[i] = hand.position.y;

                float distance = Vector3.Distance(positions[i], standing);
                if (distance < closest)
                {
                    closest = distance;
                    grabIndex = i;
                }
            }

            // Devolve o clipe ao quadro que está na tela: a amostragem acabou de percorrer a
            // animação inteira, e sem isto a janela mostraria a criatura no último quadro.
            SampleSeconds(clipTime * clip.length);

            int topIndex = grabIndex;
            for (int i = grabIndex; i <= samples; i++)
                if (height[i] > height[topIndex])
                    topIndex = i;

            // O PRIMEIRO TOPO é um pico ANTES do mais alto que volta a descer no caminho:
            // "ergueu, cedeu, ergueu mais". O degrau mínimo de 5 cm é o que separa esse gesto
            // do chacoalhar de um osso animado à mão, que produz picos de milímetros.
            int midIndex = -1;
            float bestDip = 0f;

            for (int i = grabIndex + 1; i < topIndex; i++)
            {
                if (height[i] < height[i - 1] || height[i] < height[i + 1])
                    continue;

                float valley = height[i];
                for (int j = i; j <= topIndex; j++)
                    valley = Mathf.Min(valley, height[j]);

                float dip = height[i] - valley;
                if (dip > 0.05f && dip > bestDip)
                {
                    bestDip = dip;
                    midIndex = i;
                }
            }

            secondLiftFound = midIndex > 0;

            AddPhase("a mão pega", grabIndex, samples, positions, rotations);

            if (secondLiftFound)
                AddPhase("erguida", midIndex, samples, positions, rotations);

            // topIndex == grabIndex é a mão que NUNCA sobe depois de pegar: não há topo, e
            // marcar um no mesmo quadro do bote seria inventar um momento que não existe.
            if (topIndex != grabIndex)
                AddPhase(secondLiftFound ? "erguida mais alto" : "erguida", topIndex, samples, positions, rotations);
        }

        private void AddPhase(string label, int index, int samples, Vector3[] positions, Quaternion[] rotations)
        {
            phases.Add(new HoldPhase
            {
                label = label,
                clipTime = samples > 0 ? (float)index / samples : 0f,
                handPosition = positions[index],
                handRotation = rotations[index],
            });
        }

        /// <summary>
        /// Onde a cabeça da Clear fica num momento guardado (uma fase ou um estado): o
        /// encaixe QUE VALE NAQUELE PONTO DO CLIPE, aplicado à pose guardada da mão.
        /// </summary>
        private Vector3 PhaseEye(HoldPhase phase) =>
            phase.handPosition + phase.handRotation * ResolveHandOffset(phase.clipTime);

        /// <summary>
        /// Reamostra a pose da MÃO no quadro de cada estado. Só os tempos importam aqui — o
        /// offset é aplicado na hora de desenhar —, então isto roda quando um estado é criado,
        /// movido ou apagado, e não a cada arrasto da alça.
        /// </summary>
        private void RebuildKeyPoses()
        {
            keyPoses.Clear();

            SerializedProperty keys = directorSo?.FindProperty("grabHandKeys");
            if (!previewing || hand == null || clip == null || keys == null)
                return;

            for (int i = 0; i < keys.arraySize; i++)
            {
                SerializedProperty key = keys.GetArrayElementAtIndex(i);
                float time = Mathf.Clamp01(key.FindPropertyRelative("clipTime").floatValue);

                SampleSeconds(time * clip.length);

                keyPoses.Add(new HoldPhase
                {
                    label = key.FindPropertyRelative("label").stringValue,
                    clipTime = time,
                    handPosition = hand.position,
                    handRotation = hand.rotation,
                });
            }

            // Devolve o clipe ao quadro que está na tela — a amostragem acabou de passear por
            // ele inteiro.
            SampleSeconds(clipTime * clip.length);
        }

        // --- O encaixe na mão ----------------------------------------------
        /// <summary>
        /// O ENCAIXE, que é o ajuste central da ferramenta: onde a cabeça da Clear fica em
        /// relação ao osso da mão, sempre no ESPAÇO DO OSSO.
        ///
        /// SÃO DOIS MODOS, e o segundo é o que o beat pediu:
        ///
        /// 1. UM ENCAIXE SÓ (lista vazia). Vale o clipe inteiro. Basta para a maioria dos
        ///    casos, porque o offset preso ao osso já acompanha a mão fechando, erguendo e
        ///    girando o punho.
        ///
        /// 2. ESTADOS. Um encaixe por momento do agarrão — o bote, cada levantamento —, com
        ///    o beat interpolando entre eles. É para quando o CONTATO muda de verdade ao
        ///    longo do gesto: a mão fecha no pescoço e a cabeça entra mais na palma; erguida
        ///    no alto, o corpo pende e a cabeça desce em relação ao punho.
        ///
        /// O gesto é sempre o mesmo: escolha o estado (o clipe pula para o quadro dele),
        /// arraste a alça no Scene view até a cabeça dela estar onde deveria, e confira nas
        /// duas câmeras. Os outros estados ficam na tela como fantasmas, para a coreografia
        /// inteira ser visível de uma vez.
        /// </summary>
        private void DrawAttachment()
        {
            SerializedProperty keys = directorSo.FindProperty("grabHandKeys");
            SerializedProperty offset = directorSo.FindProperty("grabHandOffset");
            if (keys == null || offset == null)
                return;

            // O GIRO fica junto do encaixe porque é o mesmo assunto — como a Clear chega até
            // a mão —, e porque ele é o primeiro a ser afinado: tudo o que vem depois é
            // julgado com o enquadramento que ele deixa.
            EditorGUILayout.LabelField("O giro até encarar a criatura", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabTurnDuration"));
            if (EditorGUI.EndChangeCheck())
            {
                directorSo.ApplyModifiedProperties();
                MarkDirty();
                beatTime = Mathf.Min(beatTime, BeatLength());
                Repaint();
            }

            EditorGUILayout.LabelField(
                "Toque o beat do início para ver: a criatura aparece PARADA no primeiro quadro e só começa a se " +
                "mexer quando o giro termina.",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encaixe na mão", EditorStyles.boldLabel);

            if (keys.arraySize == 0)
                DrawSingleOffset(keys, offset);
            else
                DrawKeyList(keys);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabAttachBlend"));
            if (EditorGUI.EndChangeCheck())
            {
                directorSo.ApplyModifiedProperties();
                MarkDirty();
                RebuildTrajectory();
                SceneView.RepaintAll();
            }

            EditorGUILayout.LabelField(
                hand != null
                    ? $"Preso em {hand.name}. Arraste a alça no Scene view — o valor é convertido para o espaço do osso."
                    : "Sem osso de mão resolvido: o encaixe não tem em que se prender.",
                EditorStyles.miniLabel);
        }

        /// <summary>O modo simples: um encaixe para o clipe inteiro, e o caminho para criar os estados.</summary>
        private void DrawSingleOffset(SerializedProperty keys, SerializedProperty offset)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(offset, new GUIContent("Offset (espaço do osso)"));
            if (EditorGUI.EndChangeCheck())
            {
                directorSo.ApplyModifiedProperties();
                MarkDirty();
                RebuildTrajectory();
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Zerar o encaixe", EditorStyles.miniButton))
                    SetSingleOffset(offset, Vector3.zero);

                if (GUILayout.Button("Pendurar 15 cm abaixo do punho", EditorStyles.miniButton))
                {
                    // No espaço do osso, "abaixo do punho" é o -Y DO OSSO. É um ponto de
                    // partida decente para um rig Mixamo e evita começar o ajuste com a
                    // cabeça da Clear dentro da mão.
                    SetSingleOffset(offset, new Vector3(0f, -0.15f, 0f));
                }
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(
                "Um encaixe só, válido no clipe inteiro. Para dar um encaixe PRÓPRIO a cada momento do agarrão — o " +
                "bote, o primeiro levantamento, o levantamento mais alto —, crie os estados a partir das fases que a " +
                "janela achou no clipe.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(phases.Count == 0))
            {
                if (GUILayout.Button($"Criar um estado em cada fase do clipe ({phases.Count})", GUILayout.Height(22)))
                    SeedKeysFromPhases(keys, offset.vector3Value);
            }
        }

        private void SetSingleOffset(SerializedProperty offset, Vector3 value)
        {
            offset.vector3Value = value;
            directorSo.ApplyModifiedProperties();
            MarkDirty();
            RebuildTrajectory();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// A lista de estados. Selecionar um LEVA O CLIPE até ele — é o gesto central: escolher
        /// o momento e ajustar o encaixe NAQUELE momento, com a alça no Scene view e as duas
        /// câmeras mostrando o resultado.
        /// </summary>
        private void DrawKeyList(SerializedProperty keys)
        {
            // A REMOÇÃO É ADIADA para depois do laço. Apagar no meio dele e sair da função
            // deixaria de desenhar controles que o evento de Layout já contou, e a Unity
            // responde a isso com "Mismatched LayoutGroup" — um erro que aparece no Console
            // sem nenhuma relação visível com o botão que foi clicado.
            int removeIndex = -1;

            for (int i = 0; i < keys.arraySize; i++)
            {
                SerializedProperty key = keys.GetArrayElementAtIndex(i);
                SerializedProperty label = key.FindPropertyRelative("label");
                SerializedProperty time = key.FindPropertyRelative("clipTime");

                bool isSelected = selectedKey == i;

                using (new EditorGUILayout.HorizontalScope(isSelected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(isSelected ? "●" : "○", EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        selectedKey = i;
                        GoTo(time.floatValue);
                    }

                    EditorGUILayout.LabelField(label.stringValue, GUILayout.MinWidth(110));
                    EditorGUILayout.LabelField($"{time.floatValue:0.00}", EditorStyles.miniLabel, GUILayout.Width(34));

                    // "aqui" = mover o estado para o quadro que está na tela. É como se marca
                    // o instante certo depois de tê-lo encontrado percorrendo.
                    if (GUILayout.Button("aqui", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        time.floatValue = clipTime;
                        ApplyKeyChange(keys);
                    }

                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0)
            {
                keys.DeleteArrayElementAtIndex(removeIndex);
                selectedKey = Mathf.Clamp(selectedKey, -1, keys.arraySize - 1);
                ApplyKeyChange(keys);

                // Encerra este OnGUI de forma limpa: a lista encolheu, e continuar
                // desenhando a partir dela é justamente o que produz o layout descasado.
                GUIUtility.ExitGUI();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ estado neste quadro", EditorStyles.miniButton))
                {
                    int index = keys.arraySize;
                    keys.InsertArrayElementAtIndex(index);

                    SerializedProperty added = keys.GetArrayElementAtIndex(index);
                    added.FindPropertyRelative("label").stringValue = $"estado {index + 1}";
                    added.FindPropertyRelative("clipTime").floatValue = clipTime;
                    added.FindPropertyRelative("offset").vector3Value = CurrentHandOffset();

                    selectedKey = index;
                    ApplyKeyChange(keys);
                }

                if (GUILayout.Button("Apagar os estados", EditorStyles.miniButton))
                {
                    if (EditorUtility.DisplayDialog(
                            "Apagar os estados",
                            "Os estados serão removidos e o beat volta a usar o Grab Hand Offset único.\n\nContinuar?",
                            "Apagar", "Cancelar"))
                    {
                        keys.arraySize = 0;
                        selectedKey = -1;
                        ApplyKeyChange(keys);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (selectedKey < 0 || selectedKey >= keys.arraySize)
            {
                EditorGUILayout.LabelField("Selecione um estado para posicionar o encaixe dele.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space(4f);

            SerializedProperty selected = keys.GetArrayElementAtIndex(selectedKey);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("label"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("clipTime"));
            EditorGUILayout.PropertyField(selected.FindPropertyRelative("offset"),
                                          new GUIContent("Offset (espaço do osso)"));
            if (EditorGUI.EndChangeCheck())
                ApplyKeyChange(keys);

            if (GUILayout.Button("Copiar este encaixe para todos os estados", EditorStyles.miniButton))
            {
                Vector3 value = selected.FindPropertyRelative("offset").vector3Value;
                for (int i = 0; i < keys.arraySize; i++)
                    keys.GetArrayElementAtIndex(i).FindPropertyRelative("offset").vector3Value = value;

                ApplyKeyChange(keys);
            }
        }

        /// <summary>
        /// Cria um estado em cada fase que a janela leu do clipe, todos partindo do encaixe
        /// que já estava afinado. É o caminho de entrada: os momentos vêm da animação, e o
        /// ajuste começa de onde já estava certo em vez de do zero.
        /// </summary>
        private void SeedKeysFromPhases(SerializedProperty keys, Vector3 seed)
        {
            keys.arraySize = phases.Count;

            for (int i = 0; i < phases.Count; i++)
            {
                SerializedProperty key = keys.GetArrayElementAtIndex(i);
                key.FindPropertyRelative("label").stringValue = phases[i].label;
                key.FindPropertyRelative("clipTime").floatValue = phases[i].clipTime;
                key.FindPropertyRelative("offset").vector3Value = seed;
            }

            selectedKey = 0;
            ApplyKeyChange(keys);
            GoTo(phases[0].clipTime);
        }

        /// <summary>
        /// Grava uma mudança na lista e ORDENA os estados pelo clipTime. A ordenação não é
        /// cosmética: o beat interpola varrendo a lista até a primeira chave adiante do
        /// quadro atual, então uma lista fora de ordem faz ele saltar encaixes — e o sintoma
        /// é a Clear pulando de posição no meio do agarrão.
        /// </summary>
        private void ApplyKeyChange(SerializedProperty keys)
        {
            directorSo.ApplyModifiedProperties();
            SortKeys(keys);
            directorSo.ApplyModifiedProperties();

            MarkDirty();
            RebuildKeyPoses();
            RebuildTrajectory();
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Insertion sort por clipTime, movendo os elementos do array serializado e
        /// carregando a seleção junto — perder de vista o estado que se está ajustando por
        /// causa de uma reordenação seria pior que a lista fora de ordem.
        /// </summary>
        private void SortKeys(SerializedProperty keys)
        {
            for (int i = 1; i < keys.arraySize; i++)
            {
                float time = keys.GetArrayElementAtIndex(i).FindPropertyRelative("clipTime").floatValue;

                int j = i;
                while (j > 0 && keys.GetArrayElementAtIndex(j - 1).FindPropertyRelative("clipTime").floatValue > time)
                {
                    keys.MoveArrayElement(j, j - 1);

                    if (selectedKey == j)
                        selectedKey = j - 1;
                    else if (selectedKey == j - 1)
                        selectedKey = j;

                    j--;
                }
            }
        }

        private void DrawDropFields()
        {
            EditorGUILayout.LabelField("A queda", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabDropGravityScale"));
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabDropSlide"));
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabGroundEyeHeight"));
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabFallenPitch"));
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabFallenRoll"));
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(2f);

            // Estes dois REPLANTAM a criatura: mudam de onde tudo o mais é medido.
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(directorSo.FindProperty("grabSpawnPoint"));

            // Com marcador, Distance e Yaw não mandam mais em nada — mostrá-los editáveis
            // convidaria a mexer num número que o beat ignora.
            bool hasMarker = directorSo.FindProperty("grabSpawnPoint")?.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(hasMarker))
            {
                EditorGUILayout.PropertyField(directorSo.FindProperty("grabDistance"));
            }

            EditorGUILayout.PropertyField(directorSo.FindProperty("grabYaw"));
            bool replant = EditorGUI.EndChangeCheck();

            if (hasMarker)
            {
                EditorGUILayout.LabelField(
                    "A pose vem do marcador; o Grab Distance está desligado. O Grab Yaw ainda soma por cima — " +
                    "deixe em 0 se a rotação do marcador já é a final.",
                    EditorStyles.miniLabel);
            }

            if (changed || replant)
            {
                directorSo.ApplyModifiedProperties();
                MarkDirty();

                if (replant)
                {
                    PlantCreature();
                    Sample(clipTime);

                    // As fases guardam a pose da mão EM MUNDO: mover a criatura invalida
                    // todas elas de uma vez.
                    RebuildPhases();
                    RebuildKeyPoses();
                }

                RebuildTrajectory();
                SceneView.RepaintAll();
            }
        }

        private void DrawReadout()
        {
            if (!previewing || hand == null)
                return;

            if (!trajectoryValid)
            {
                EditorGUILayout.HelpBox("Sem ponto de soltura válido para calcular a queda.", MessageType.Warning);
                return;
            }

            float height = fallStart.y - landingFeet.y;
            float handHeight = heldFeet.y - landingFeet.y;

            EditorGUILayout.LabelField("No quadro em que a mão abre", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Osso da mão:  {hand.name}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Pés a:  {handHeight:0.00} m do chão", EditorStyles.miniLabel);
            float authored = FallDuration();
            EditorGUILayout.LabelField($"Queda:  {height:0.00} m em {flightDuration:0.00} s " +
                                       $"({(authored > 0f ? "prazo do Inspector" : "queda livre")})" +
                                       $"   —   {Mathf.Abs(releaseVerticalSpeed):0.0} m/s, constante" +
                                       $"   —   beat inteiro: {BeatLength():0.00} s");

            // O TETO MORDENDO É DITO EM VOZ ALTA. Ele mordia calado, e o efeito era esta
            // janela informar uma duração que a cena não tinha — a ferramenta de ajuste
            // escondendo justamente o que estava errado.
            if (authored <= 0f && freeFallSolved > MaxFreeFall)
            {
                EditorGUILayout.HelpBox(
                    $"Em queda livre estes {height:0.0} m levariam {freeFallSolved:0.0} s. É mais que o teto de " +
                    $"{MaxFreeFall:0} s, então o beat desce a {Mathf.Abs(releaseVerticalSpeed):0.0} m/s para caber " +
                    $"em {flightDuration:0.0} s. Se a " +
                    "distância é de propósito, preencha o Grab Fall Duration com o tempo que a cena quer — é o " +
                    "campo feito para isso, e aí o número acima passa a ser seu.",
                    MessageType.Warning);
            }

            DrawFallMarkers();

            // A ALTURA É O ÚNICO NÚMERO QUE DECIDE A QUEDA — dito em voz alta porque é ele
            // que denuncia um encaixe errado. Não há campo de altura em lugar nenhum, então
            // esta linha é, ao mesmo tempo, a duração da queda e o teste do Grab Hand Offset.
            if (height < 0.35f)
            {
                EditorGUILayout.HelpBox(
                    $"A queda tem {height:0.00} m — praticamente nenhuma. Se não há marcadores, é o Grab Hand " +
                    "Offset (ou a última chave de encaixe) pendurando a Clear abaixo do punho; se há, é o Fall " +
                    "Start Point quase encostado no Fall Landing Point.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// O estado dos dois marcadores da queda, e o botão que os cria JÁ NO LUGAR.
        ///
        /// POR QUE O BOTÃO EXISTE: os dois pontos são fáceis de descrever e impossíveis de
        /// achar a olho. O de partida fica onde o punho da criatura está no QUADRO DA SOLTURA
        /// (o Grab Release Normalized Time) — uma pose que não existe na cena parada, só durante a animação, e que esta
        /// janela é a única coisa do projeto que sabe amostrar. Sem o botão, "crie um vazio
        /// de referência" vira arrastar um objeto no escuro até a queda parecer certa.
        ///
        /// CRIADO É IGUAL A CALCULADO, de propósito: o botão grava exatamente os pontos que o
        /// beat usaria sem marcador nenhum. Então clicar nele não muda a cena em nada — ele
        /// só transforma uma conta automática em duas coisas que se pode PEGAR e arrastar. A
        /// partir daí a queda é ajustada com a mão, na Scene View, que é o ponto.
        /// </summary>
        private void DrawFallMarkers()
        {
            var start = directorSo.FindProperty("fallStartPoint")?.objectReferenceValue as Transform;
            var land = directorSo.FindProperty("fallLandingPoint")?.objectReferenceValue as Transform;

            EditorGUILayout.LabelField(
                $"Marcadores:  parte de {(start != null ? start.name : "onde a mão largou")}" +
                $"   —   pousa em {(land != null ? land.name : "cálculo automático")}",
                EditorStyles.miniLabel);

            // O SALTO É DITO AQUI, e não só no Console em play: é a única coisa que pode dar
            // errado com um marcador de partida, e ela aparece por um frame só.
            float jump = Vector3.Distance(heldFeet, fallStart);
            if (start != null && jump > 1f)
            {
                EditorGUILayout.HelpBox(
                    $"O Fall Start Point está a {jump:0.0} m da mão da criatura: a Clear vai SALTAR para lá no " +
                    "quadro da soltura. De propósito, tudo bem; senão, use o botão abaixo para pô-lo de volta na mão.",
                    MessageType.Warning);
            }

            if (GUILayout.Button(start == null && land == null
                    ? "Criar os marcadores da queda aqui"
                    : "Recolocar os marcadores no cálculo automático"))
            {
                CreateFallMarkers();
            }
        }

        /// <summary>
        /// Cria (ou reposiciona) os dois vazios e os liga no director, tudo em um Undo só.
        ///
        /// OS PONTOS SÃO OS PÉS, não os olhos — é o que o director escreve em
        /// <c>body.position</c>. Gravar a altura do olhar deixaria a Clear uma cabeça acima do
        /// que o marcador mostra, e o erro é do tipo que só aparece em play.
        ///
        /// NASCEM IRMÃOS DA MARCAÇÃO ABYSS: é onde o resto da coreografia deste beat mora
        /// (o GrabPoint, o fim do corredor), então é onde alguém vai procurá-los. Sem a marca,
        /// nascem na raiz da cena, que é feio mas achável.
        ///
        /// O POUSO É CALCULADO A PARTIR DO PONTO DE PARTIDA, e não da posição atual do
        /// marcador de pouso: senão recolocar os dois de uma vez faria o pouso sair de si
        /// mesmo, e o botão deixaria de devolver o cálculo automático que promete.
        /// </summary>
        private void CreateFallMarkers()
        {
            Transform parent = null;
            if (PesadeloGrabSetup.TryGetCorridorAxis(directorSo, out _, out Transform abyss) && abyss != null)
                parent = abyss.parent;

            Transform start = EnsureMarker("fallStartPoint", "FallStartPoint", parent, heldFeet);
            Transform land = EnsureMarker("fallLandingPoint", "FallLandingPoint", parent, LandingFeet(heldFeet));

            directorSo.ApplyModifiedProperties();

            RebuildTrajectory();
            Selection.objects = new Object[] { start.gameObject, land.gameObject };
            SceneView.RepaintAll();

            Debug.Log($"[PesadeloGrabPreviewWindow] \"{start.name}\" e \"{land.name}\" posicionados e ligados no " +
                      "director. Arraste-os na Scene View para ajustar a queda — a janela recalcula sozinha. " +
                      "SALVE A CENA depois.", start);
        }

        /// <summary>
        /// O vazio de um dos extremos: reaproveita o que já estiver ligado no campo, senão
        /// cria. Reaproveitar importa porque o botão também serve de "desfazer meus ajustes" —
        /// e criar um segundo objeto a cada clique deixaria a cena cheia de marcadores órfãos
        /// com o mesmo nome.
        /// </summary>
        private Transform EnsureMarker(string property, string name, Transform parent, Vector3 position)
        {
            SerializedProperty field = directorSo.FindProperty(property);
            var marker = field?.objectReferenceValue as Transform;

            if (marker == null)
            {
                var created = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(created, "Criar marcadores da queda");
                marker = created.transform;

                if (parent != null)
                    Undo.SetTransformParent(marker, parent, "Criar marcadores da queda");

                if (field != null)
                    field.objectReferenceValue = marker;
            }

            Undo.RecordObject(marker, "Posicionar marcadores da queda");
            marker.position = position;

            // O GIRO NÃO É USADO pelo beat — só a posição —, mas um marcador torto na Scene
            // View sugere que ele significa alguma coisa. Zerado, ele é o que é: um ponto.
            marker.rotation = Quaternion.identity;

            return marker;
        }

        // --- Reprodução ----------------------------------------------------

        private void TogglePlay()
        {
            playing = !playing;
            if (!playing)
                return;

            if (beatTime >= BeatLength())
                beatTime = 0f;

            lastTick = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// O pulso da reprodução. NÃO se chama Update de propósito: o EditorWindow tem um
        /// Update mágico que a Unity chama sozinha, e um método com esse nome ficaria
        /// inscrito duas vezes — uma pela Unity, outra pelo EditorApplication.
        /// </summary>
        private void OnEditorTick()
        {
            if (!playing || !previewing)
                return;

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastTick);
            lastTick = now;

            // Um delta gigante (a janela ficou em segundo plano, uma importação travou o
            // editor) faria o beat pular direto para o pouso. O teto transforma isso numa
            // reprodução lenta em vez de num salto.
            beatTime += Mathf.Min(delta, 0.05f);
            if (beatTime >= BeatLength())
                beatTime = 0f;

            ApplyBeatTime();
            Repaint();
        }

        private void ApplyBeatTime()
        {
            if (clip == null)
                return;

            clipTime = Mathf.Clamp01(ClipSeconds() / Mathf.Max(0.0001f, clip.length));
            Sample(clipTime);
        }

        private void GoTo(float normalized)
        {
            playing = false;
            clipTime = Mathf.Clamp01(normalized);
            beatTime = TurnDuration() + clipTime * clip.length;
            Sample(clipTime);
        }

        /// <summary>
        /// O GIRO É UM PRÉ-ROLO do beat: durante ele a criatura está congelada no primeiro
        /// quadro (o director segura o Animator em speed 0), então o relógio do CLIPE só
        /// começa a andar depois que a Clear terminou de virar.
        ///
        /// Reproduzir o giro aqui não é enfeite: "leve, nem rápido nem lerdo" é uma decisão
        /// que só se toma vendo, e vendo pelos olhos dela.
        /// </summary>
        private float ClipSeconds() => Mathf.Max(0f, beatTime - TurnDuration());

        private float BeatLength()
        {
            float linger = directorSo?.FindProperty("grabLingerDuration")?.floatValue ?? 0.7f;
            float afterTurn = Mathf.Max(clip != null ? clip.length : 1f,
                                        ReleaseSeconds() + flightDuration + linger);

            return TurnDuration() + afterTurn;
        }

        private string PhaseName()
        {
            if (beatTime < TurnDuration())
                return "girando para a criatura";

            if (!trajectoryValid)
                return "sem trajetória";

            float clock = ClipSeconds();
            float release = ReleaseSeconds();
            if (clock >= release)
                return clock < release + flightDuration ? "caindo" : "caída no chão";

            if (clock < AttachBlend())
                return "puxada até a mão";

            // O rótulo da última fase já alcançada: é o que dá nome ao que se está vendo
            // ("erguida", "erguida mais alto") sem que ninguém tenha escrito esses momentos.
            string label = "presa na mão";
            foreach (HoldPhase phase in phases)
                if (phase.clipTime <= clipTime)
                    label = phase.label;

            return label;
        }

        // --- Preview -------------------------------------------------------

        private void StartPreview()
        {
            if (grab == null || clip == null)
                return;

            savedActive = grab.activeSelf;
            savedPosition = grab.transform.position;
            savedRotation = grab.transform.rotation;

            grab.SetActive(true);
            PlantCreature();

            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();

            previewing = true;
            EditorApplication.update += OnEditorTick;

            ResolveBones();
            Sample(clipTime);
            RebuildPhases();
            RebuildKeyPoses();
            RebuildTrajectory();
        }

        private void StopPreview()
        {
            EditorApplication.update -= OnEditorTick;
            DestroyCameras();

            if (!previewing)
                return;

            previewing = false;
            playing = false;

            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            if (grab != null)
            {
                grab.transform.SetPositionAndRotation(savedPosition, savedRotation);
                grab.SetActive(savedActive);
            }

            hand = null;
            head = null;
            trajectoryValid = false;
            trajectory.Clear();
            SceneView.RepaintAll();
        }

        private void PlantCreature()
        {
            if (grab != null && PesadeloGrabSetup.TryGetStagingPose(directorSo, out Vector3 position, out Quaternion rotation))
                grab.transform.SetPositionAndRotation(position, rotation);
        }

        private void Sample(float normalized)
        {
            if (!previewing || grab == null || clip == null)
                return;

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(grab, clip, Mathf.Clamp01(normalized) * clip.length);
            AnimationMode.EndSampling();

            SceneView.RepaintAll();
        }

        private void StepFrame(int direction)
        {
            float frame = 1f / Mathf.Max(1f, clip.length * clip.frameRate);
            GoTo(clipTime + direction * frame);
        }

        private void ResolveBones()
        {
            hand = directorSo.FindProperty("grabHandBone")?.objectReferenceValue as Transform;
            head = directorSo.FindProperty("grabHeadBone")?.objectReferenceValue as Transform;

            if (grab == null)
                return;

            Transform[] bones = grab.GetComponentsInChildren<Transform>(includeInactive: true);

            if (hand == null)
                hand = FindBone(bones, "righthand") ?? FindBone(bones, "lefthand") ?? FindBone(bones, "hand");

            // Mesma correção do director: com a Clear encaixada na mão, um Grab Head Bone
            // apontando para a própria mão não dá direção de olhar nenhuma.
            if (head != null && head == hand)
                head = null;

            if (head == null)
                head = FindBone(bones, "head");
        }

        /// <summary>
        /// Mesma regra do director: EndsWith primeiro, nome mais curto depois. Os DEDOS têm
        /// "hand" no nome em rig Mixamo, e pendurar a Clear numa falange quase funciona.
        /// </summary>
        private static Transform FindBone(Transform[] bones, string fragment)
        {
            Transform shortest = null;

            foreach (Transform bone in bones)
            {
                if (bone == null)
                    continue;

                string name = bone.name.ToLowerInvariant();
                if (!name.Contains(fragment))
                    continue;

                if (name.EndsWith(fragment))
                    return bone;

                if (shortest == null || bone.name.Length < shortest.name.Length)
                    shortest = bone;
            }

            return shortest;
        }
        // --- Pose da Clear, espelhando o director ---------------------------

        /// <summary>
        /// Onde a Clear está NESTE instante do beat: encaixada na mão enquanto a mão está
        /// fechada, caindo depois que ela abre. É a mesma composição do <c>UpdateGrip</c> e
        /// do <c>FallFromGrab</c> do director — se as duas discordassem, a ferramenta estaria
        /// ajustando uma cena que não é a que roda.
        /// </summary>
        private Vector3 EyeAtBeatTime()
        {
            float clock = ClipSeconds();

            if (trajectoryValid && clock >= ReleaseSeconds())
            {
                // A VELOCIDADE VEM DO releaseVerticalSpeed, e não é recalculada aqui: ela sai
                // da altura e do prazo já resolvidos no RebuildTrajectory, e refazer a conta
                // neste ponto reintroduziria a discordância entre a queda desenhada e a queda
                // que roda.
                return FallingEye(Mathf.Min(clock - ReleaseSeconds(), flightDuration), fallStart, EyeHeight());
            }

            // Durante o giro ela ainda está DE PÉ na marca: a mão não chegou, e a puxada só
            // começa quando o clipe começa.
            return beatTime < TurnDuration() ? StandingEye() : HeldEye(clock);
        }

        /// <summary>
        /// A pose presa: o ENCAIXE NO OSSO, com a puxada do começo — os primeiros
        /// <c>grabAttachBlend</c> segundos, em que a Clear ainda está saindo de onde estava
        /// de pé. É o trecho em que o susto acontece, então o preview precisa mostrá-lo.
        /// </summary>
        private Vector3 HeldEye(float seconds)
        {
            Vector3 attached = HandAttachEye();

            float blend = AttachBlend();
            if (blend <= 0f)
                return attached;

            float entry = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(seconds / blend));
            return Vector3.Lerp(StandingEye(), attached, entry);
        }

        /// <summary>
        /// O encaixe: a posição do osso mais o offset GIRADO POR ELE. Espelha o
        /// <c>HandAttachEye</c> do director, linha por linha — inclusive o cuidado de usar
        /// <c>rotation *</c> em vez de <c>TransformPoint</c>, para a escala do rig não
        /// multiplicar um offset que está em metros.
        /// </summary>
        private Vector3 HandAttachEye()
        {
            if (hand == null)
                return grab != null ? grab.transform.position : Vector3.zero;

            return hand.position + hand.rotation * CurrentHandOffset();
        }

        /// <summary>O encaixe que vale no quadro que está na tela.</summary>
        private Vector3 CurrentHandOffset() => ResolveHandOffset(clipTime);

        /// <summary>
        /// O encaixe num ponto do clipe, interpolado entre os estados vizinhos com SmoothStep
        /// e estendido nas pontas. Espelha o <c>ResolveHandOffset</c> do director — se as duas
        /// contas discordassem, a ferramenta estaria ajustando uma cena que não é a que roda.
        /// </summary>
        private Vector3 ResolveHandOffset(float normalized)
        {
            SerializedProperty keys = directorSo?.FindProperty("grabHandKeys");
            if (keys == null || keys.arraySize == 0)
                return HandOffset();

            int next = -1;
            for (int i = 0; i < keys.arraySize; i++)
            {
                if (KeyTime(keys, i) >= normalized)
                {
                    next = i;
                    break;
                }
            }

            if (next <= 0)
                return KeyOffset(keys, next == 0 ? 0 : keys.arraySize - 1);

            float a = KeyTime(keys, next - 1);
            float b = KeyTime(keys, next);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((normalized - a) / Mathf.Max(0.0001f, b - a)));

            return Vector3.Lerp(KeyOffset(keys, next - 1), KeyOffset(keys, next), k);
        }

        private static float KeyTime(SerializedProperty keys, int index) =>
            keys.GetArrayElementAtIndex(index).FindPropertyRelative("clipTime").floatValue;

        private static Vector3 KeyOffset(SerializedProperty keys, int index) =>
            keys.GetArrayElementAtIndex(index).FindPropertyRelative("offset").vector3Value;

        private Vector3 StandingEye() => StandingFeet() + Vector3.up * EyeHeight();

        /// <summary>
        /// A inclinação do olhar no instante atual. Presa na mão, NENHUMA: quem manda no
        /// enquadramento é a mira na cara da criatura, e herdar o giro do punho rodaria a
        /// câmera do jogador.
        ///
        /// CAINDO, SÃO DOIS TEMPOS, os mesmos do <c>FallFromGrab</c> do director: o olhar
        /// vira PARA CIMA no primeiro quarto do trajeto (ela foi solta, e o que fica em
        /// quadro é a criatura ficando para trás lá em cima) e assenta na pose caída nos
        /// últimos décimos. Reproduzir os dois aqui é o que deixa ver, pelos olhos dela, se
        /// a criatura continua enquadrada durante a queda e como o último frame do pesadelo
        /// fica.
        /// </summary>
        private Quaternion CurrentTilt()
        {
            float clock = ClipSeconds();

            if (!trajectoryValid || clock < ReleaseSeconds())
                return Quaternion.identity;

            float t = Mathf.Min(clock - ReleaseSeconds(), flightDuration);
            Quaternion diving = Quaternion.Euler(FallLookPitch(), 0f, 0f);
            float slam = SlamDuration();

            if (t < flightDuration - slam)
            {
                float aim = AimDuration();
                float k = aim <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / aim));
                return Quaternion.SlerpUnclamped(Quaternion.identity, diving, k);
            }

            float slammed = slam <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (flightDuration - slam)) / slam));

            return Quaternion.SlerpUnclamped(diving, FallenRotation(), slammed);
        }

        /// <summary>
        /// QUANTO DO GIRO já foi feito, de 0 a 1, com a mesma SmoothStep do director. É o que
        /// faz a câmera da Clear virar aqui igual ao que ela vira em play.
        /// </summary>
        private float TurnProgress()
        {
            float duration = TurnDuration();
            if (duration <= 0f)
                return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(beatTime / duration));
        }

        private float TurnDuration() =>
            Mathf.Max(0f, directorSo?.FindProperty("grabTurnDuration")?.floatValue ?? 0.5f);

        /// <summary>Para onde ela está olhando ANTES do giro: o eixo do corredor, com o olhar nivelado.</summary>
        private Quaternion StandingLook()
        {
            PesadeloGrabSetup.TryGetCorridorAxis(directorSo, out Vector3 axis, out _);
            axis.y = 0f;

            return axis.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(axis.normalized, Vector3.up);
        }
        // --- A queda -------------------------------------------------------

        /// <summary>
        /// Recalcula a queda: de onde a mão larga a Clear, quanto tempo ela leva para
        /// chegar ao chão e por onde ela passa no caminho. Mesma conta do <c>SolveDrop</c> do
        /// director — descida em velocidade constante, com o tempo saindo da altura (ou do
        /// prazo autorado).
        /// </summary>
        private void RebuildTrajectory()
        {
            trajectoryValid = false;
            trajectory.Clear();

            if (!previewing || hand == null || clip == null || directorSo == null)
                return;

            float release = Mathf.Clamp01(directorSo.FindProperty("grabReleaseNormalizedTime")?.floatValue ?? 0f);
            if (release <= 0f)
                return;

            float eyeHeight = EyeHeight();
            float releaseSeconds = release * clip.length;

            // Amostra NO QUADRO DA SOLTURA para ler o osso ali, e devolve o clipe ao quadro
            // que está na tela: sem isso a janela mostraria a criatura numa pose e as contas
            // mediriam outra.
            //
            // A QUEDA PARTE DAQUI — de onde a animação deixou a Clear, e não de uma altura
            // escolhida no Inspector. É o que o director faz: ele lê a posição do jogador no
            // frame em que o clipe acaba. Logo, quem decide de que altura ela cai é o ENCAIXE
            // (o Grab Hand Offset e as chaves), e um encaixe errado aparece aqui como uma
            // queda de duração errada — que é justamente o que esta janela existe para achar.
            SampleSeconds(releaseSeconds);
            releaseEye = HeldEye(releaseSeconds);
            heldFeet = releaseEye - Vector3.up * eyeHeight;
            SampleSeconds(clipTime * clip.length);

            // OS MARCADORES TÊM PRECEDÊNCIA, igual ao director. Se a janela desenhasse a
            // queda automática enquanto o beat cai entre dois marcadores, o ajuste estaria
            // sendo feito contra uma cena que não é a que roda — o mesmo cuidado que o
            // TryGetStagingPose toma com o Grab Spawn Point.
            fallStart = MarkerOr("fallStartPoint", heldFeet);
            landingFeet = MarkerOr("fallLandingPoint", LandingFeet(fallStart));

            // A MESMA DIVISÃO DO SolveDrop do director: prazo autorado manda no tempo, e sem
            // ele o tempo sai da altura. A VELOCIDADE É CONSTANTE nos dois, e é o que a linha
            // desenhada aqui precisa mostrar — uma janela que desenhasse a parábola antiga
            // enquanto o beat desce em ritmo único ajustaria uma cena que não existe. Foi
            // assim durante toda a vida do teto de 3 s.
            float height = Mathf.Max(0f, fallStart.y - landingFeet.y);
            float authored = FallDuration();

            if (authored > 0f)
            {
                flightDuration = Mathf.Max(0.15f, authored);
            }
            else
            {
                float world = Physics.gravity.y * Mathf.Max(0.05f, GravityScale());
                float solved = height > 0f ? Mathf.Sqrt(2f * height / Mathf.Abs(world)) : 0f;
                freeFallSolved = solved;

                flightDuration = Mathf.Clamp(solved, 0.15f, MaxFreeFall);
            }

            releaseVerticalSpeed = SpeedFor(height, flightDuration);

            for (int i = 0; i <= TrajectorySamples; i++)
            {
                float t = flightDuration * i / TrajectorySamples;
                trajectory.Add(FallingEye(t, fallStart, eyeHeight));
            }

            trajectoryValid = true;
        }

        /// <summary>
        /// O olho durante a queda. O corpo desce em LINHA RETA, a velocidade constante; o
        /// olho fica na ALTURA NORMAL o caminho todo e só desce para a altura de alguém caída
        /// no tombo do fim (<see cref="SlamDuration"/>) — caindo ela ainda é alguém de pé, e é
        /// o impacto que a põe no chão. Mesma divisão do <c>FallFromGrab</c> do director.
        /// </summary>
        private Vector3 FallingEye(float t, Vector3 bodyStart, float eyeHeight)
        {
            float slide = Mathf.Clamp01(t / Mathf.Max(0.0001f, flightDuration));

            Vector3 body = new Vector3(
                Mathf.Lerp(bodyStart.x, landingFeet.x, slide),
                bodyStart.y + releaseVerticalSpeed * t,
                Mathf.Lerp(bodyStart.z, landingFeet.z, slide));

            float slam = SlamDuration();
            float k = slam <= 0f || t < flightDuration - slam
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (flightDuration - slam)) / slam));

            return body + Vector3.up * Mathf.Lerp(eyeHeight, GroundEyeHeight(), k);
        }

        // --- Medidas -------------------------------------------------------

        private Vector3 HandOffset() => directorSo.FindProperty("grabHandOffset")?.vector3Value ?? Vector3.zero;
        private float AttachBlend() => Mathf.Max(0f, directorSo.FindProperty("grabAttachBlend")?.floatValue ?? 0.18f);
        private float GravityScale() => directorSo.FindProperty("grabDropGravityScale")?.floatValue ?? 0.4f;
        private float FallLookPitch() => directorSo.FindProperty("grabFallLookPitch")?.floatValue ?? -50f;
        private float FallDuration() => directorSo.FindProperty("grabFallDuration")?.floatValue ?? 0f;

        /// <summary>
        /// O teto da queda livre e a velocidade constante da descida, os dois iguais aos do
        /// director. Duplicados porque esta janela precisa desenhar a MESMA queda — uma
        /// ferramenta que discorda do beat ajusta uma cena que não existe.
        /// </summary>
        private const float MaxFreeFall = 30f;

        private static float SpeedFor(float height, float fall)
        {
            if (height <= 0f || fall <= 0f)
                return 0f;

            return -height / fall;
        }

        /// <summary>
        /// A posição do marcador <paramref name="property"/>, ou <paramref name="fallback"/>
        /// quando o campo está vazio. É a regra do beat inteiro em uma linha: marcador manda,
        /// conta automática é rede de segurança.
        /// </summary>
        private Vector3 MarkerOr(string property, Vector3 fallback)
        {
            var marker = directorSo.FindProperty(property)?.objectReferenceValue as Transform;
            return marker != null ? marker.position : fallback;
        }

        /// <summary>
        /// Os dois tempos do olhar durante a queda, com os mesmos números do
        /// <c>FallFromGrab</c>: um quarto do trajeto para virar para cima, um décimo e meio
        /// para o tombo do impacto — os dois com teto em segundos, senão uma soltura muito
        /// alta gastaria segundos girando a câmera. Duplicados aqui porque são constantes do
        /// director, e um campo no Inspector para cada um deles seria dar nome de ajuste a
        /// duas decisões que ninguém precisa reabrir.
        /// </summary>
        private float AimDuration() => Mathf.Min(flightDuration * 0.25f, 0.6f);
        private float SlamDuration() => Mathf.Min(flightDuration * 0.15f, 0.18f);
        private float GroundEyeHeight() => Mathf.Max(0.05f, directorSo.FindProperty("grabGroundEyeHeight")?.floatValue ?? 0.25f);
        private float FallenPitch() => directorSo.FindProperty("grabFallenPitch")?.floatValue ?? -35f;
        private float FallenRoll() => directorSo.FindProperty("grabFallenRoll")?.floatValue ?? 22f;
        private Quaternion FallenRotation() => Quaternion.Euler(FallenPitch(), 0f, FallenRoll());

        /// <summary>
        /// A fração do clipe que a PUXADA ocupa: até aí a Clear ainda está chegando à mão, e
        /// soltar nesse trecho é largar alguém que a criatura não pegou. Mesma conta do
        /// <c>ReleaseClipTime</c> do director, teto de 0,5 incluído.
        /// </summary>
        private float ReleaseFloor()
        {
            float length = clip != null ? clip.length : 0f;
            return length > 0.0001f ? Mathf.Clamp(AttachBlend() / length, 0.01f, 0.5f) : 0.01f;
        }

        private float ReleaseSeconds()
        {
            float release = Mathf.Clamp01(directorSo?.FindProperty("grabReleaseNormalizedTime")?.floatValue ?? 0f);
            return release * (clip != null ? clip.length : 0f);
        }

        private float EyeHeight()
        {
            var player = directorSo.FindProperty("playerController")?.objectReferenceValue as PlayerController;
            Transform holder = player != null ? player.CameraHolder : null;
            return holder != null ? holder.localPosition.y : 1.6f;
        }

        /// <summary>Onde ela está DE PÉ quando a criatura chega: a marcação Abyss, de onde a puxada parte.</summary>
        private Vector3 StandingFeet()
        {
            PesadeloGrabSetup.TryGetCorridorAxis(directorSo, out _, out Transform abyss);
            return abyss != null ? abyss.position : grab.transform.position;
        }

        /// <summary>
        /// Onde ela para: DEBAIXO de onde a mão a soltou, escorregada o Grab Drop Slide
        /// para trás. Espelha o <c>LandingPosition</c> do director, que mede a partir do
        /// ponto da SOLTURA — ela é largada, e cai onde estava.
        /// </summary>
        private Vector3 LandingFeet(Vector3 releasedFeet)
        {
            float slide = directorSo.FindProperty("grabDropSlide")?.floatValue ?? 0.25f;

            PesadeloGrabSetup.TryGetCorridorAxis(directorSo, out Vector3 axis, out _);
            Vector3 back = -axis;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0001f)
                back = Vector3.back;

            Vector3 spot = releasedFeet + back.normalized * Mathf.Max(0f, slide);

            // O Y sai do CHÃO DA MARCA, como no director: um raycast lançado do ar erra o
            // piso quando a criatura ergue a Clear alto, e o preview mostraria uma queda que
            // termina no lugar errado.
            Vector3 standing = StandingFeet();
            return new Vector3(spot.x, standing.y, spot.z);
        }

        private void SampleSeconds(float seconds)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(grab, clip, Mathf.Clamp(seconds, 0f, clip.length));
            AnimationMode.EndSampling();
        }

        // --- Scene view ----------------------------------------------------

        /// <summary>
        /// Desenha a Clear onde ela está agora, a linha até o osso que a segura, a queda e o
        /// ponto em que ela para no chão.
        ///
        /// A LINHA PONTILHADA ATÉ A MÃO é o que se olha ao ajustar: ela mostra o encaixe em
        /// pessoa. Percorrendo o clipe, ela deve manter o mesmo comprimento e a mesma direção
        /// EM RELAÇÃO À MÃO do começo ao fim — se ela cresce ou vira quando o punho gira, o
        /// offset foi parar num eixo que não é o do osso.
        /// </summary>
        private void OnSceneGUI(SceneView view)
        {
            if (!previewing || hand == null || directorSo == null)
                return;

            directorSo.Update();

            Vector3 eye = EyeAtBeatTime();
            float eyeHeight = EyeHeight();
            Vector3 feet = eye - Vector3.up * eyeHeight;
            bool held = !trajectoryValid || beatTime < ReleaseSeconds();

            DrawPhaseGhosts(eyeHeight);

            if (trajectoryValid && trajectory.Count > 1)
            {
                Handles.color = new Color(1f, 0.35f, 0.35f, 0.9f);
                Handles.DrawAAPolyLine(3f, trajectory.ToArray());
                Handles.color = new Color(1f, 0.35f, 0.35f, 0.5f);
                Handles.DrawWireDisc(landingFeet, Vector3.up, 0.3f);
                Handles.Label(landingFeet + Vector3.up * 0.2f, $"caída — {flightDuration:0.00} s de queda");
            }

            Handles.color = held ? new Color(0.3f, 0.9f, 1f, 1f) : new Color(1f, 0.6f, 0.3f, 1f);
            Handles.DrawLine(eye, feet);
            Handles.DrawWireDisc(feet, Vector3.up, 0.25f);
            Handles.SphereHandleCap(0, eye, Quaternion.identity, 0.12f, EventType.Repaint);

            if (held)
            {
                Handles.color = new Color(1f, 0.5f, 0.1f, 0.9f);
                Handles.DrawDottedLine(hand.position, eye, 3f);
                Handles.SphereHandleCap(0, hand.position, Quaternion.identity, 0.04f, EventType.Repaint);
            }

            Handles.Label(eye + Vector3.up * 0.25f, $"Clear — {PhaseName()}");

            DrawOffsetHandle();
        }

        /// <summary>
        /// OS FANTASMAS DA COREOGRAFIA: onde a Clear vai estar em cada fase do clipe, todas
        /// na tela ao mesmo tempo, ligadas na ordem em que acontecem.
        ///
        /// É o que faz o segundo levantamento existir aos olhos em vez de existir só no
        /// clipe: dá para ver de uma vez que ela sobe, cede e sobe MAIS ALTO, e a que altura
        /// do chão cada um desses momentos deixa a cabeça dela — sem tocar o beat inteiro.
        ///
        /// Eles seguem o encaixe: arraste a alça e os três se movem junto, porque o que está
        /// guardado é a pose da mão, não a da Clear.
        /// </summary>
        private void DrawPhaseGhosts(float eyeHeight)
        {
            // COM ESTADOS, os fantasmas são os ESTADOS: são eles que estão sendo ajustados, e
            // desenhar as fases por cima encheria a tela de bolinhas quase coincidentes. Sem
            // estados, as fases lidas do clipe fazem o mesmo trabalho de mostrar a
            // coreografia — só que sem nada para arrastar.
            List<HoldPhase> ghosts = keyPoses.Count > 0 ? keyPoses : phases;
            if (ghosts.Count == 0)
                return;

            Vector3 previous = Vector3.zero;
            bool hasPrevious = false;

            for (int i = 0; i < ghosts.Count; i++)
            {
                HoldPhase ghost = ghosts[i];
                Vector3 ghostEye = PhaseEye(ghost);
                bool isSelected = keyPoses.Count > 0 && i == selectedKey;

                Handles.color = isSelected
                    ? new Color(0.4f, 1f, 0.8f, 0.9f)
                    : new Color(0.5f, 0.8f, 1f, 0.35f);

                Handles.DrawLine(ghostEye, ghostEye - Vector3.up * eyeHeight);
                Handles.SphereHandleCap(0, ghostEye, Quaternion.identity, isSelected ? 0.1f : 0.07f, EventType.Repaint);
                Handles.Label(ghostEye + Vector3.right * 0.15f, $"{ghost.label}  ({ghost.clipTime:0.00})");

                if (hasPrevious)
                {
                    Handles.color = new Color(0.5f, 0.8f, 1f, 0.3f);
                    Handles.DrawDottedLine(previous, ghostEye, 2f);
                }

                previous = ghostEye;
                hasPrevious = true;
            }
        }

        /// <summary>
        /// A ALÇA DO ENCAIXE. Arrastar move a Clear em relação à mão e grava o resultado no
        /// Grab Hand Offset, convertido para o ESPAÇO DO OSSO — senão o número gravado não
        /// corresponderia ao que foi arrastado no quadro seguinte, quando o punho já girou.
        ///
        /// Só aparece enquanto ela está presa: depois da soltura não há encaixe nenhum para
        /// ajustar, e uma alça ali só serviria para mover a Clear por engano.
        /// </summary>
        private void DrawOffsetHandle()
        {
            if (hand == null)
                return;

            if (trajectoryValid && beatTime >= ReleaseSeconds())
                return;

            SerializedProperty keys = directorSo.FindProperty("grabHandKeys");
            bool hasKeys = keys != null && keys.arraySize > 0;

            // COM ESTADOS, a alça fica na pose da mão DO ESTADO SELECIONADO, esteja o clipe
            // no quadro que estiver. É o que garante que o arrasto seja convertido pela
            // rotação de punho daquele momento: medido pela pose de outro quadro, o encaixe
            // gravado sairia torto exatamente pelo tanto que o punho girou entre os dois.
            SerializedProperty offset;
            Vector3 attached;
            Quaternion handRotation;

            if (hasKeys)
            {
                if (selectedKey < 0 || selectedKey >= keys.arraySize || selectedKey >= keyPoses.Count)
                    return;

                HoldPhase pose = keyPoses[selectedKey];
                offset = keys.GetArrayElementAtIndex(selectedKey).FindPropertyRelative("offset");
                attached = PhaseEye(pose);
                handRotation = pose.handRotation;
            }
            else
            {
                offset = directorSo.FindProperty("grabHandOffset");
                if (offset == null)
                    return;

                attached = HandAttachEye();
                handRotation = hand.rotation;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(attached, handRotation);
            if (!EditorGUI.EndChangeCheck())
                return;

            // Inverse(rotation) e não InverseTransformPoint: a escala do rig não pode entrar
            // no offset, pelo mesmo motivo que ela não entra na hora de aplicá-lo.
            offset.vector3Value += Quaternion.Inverse(handRotation) * (moved - attached);
            directorSo.ApplyModifiedProperties();
            MarkDirty();
            RebuildTrajectory();
            Repaint();
        }

        // --- Utilidades ----------------------------------------------------

        private void MarkDirty()
        {
            if (director == null)
                return;

            EditorUtility.SetDirty(director);
            if (director.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        }

        private void OnSelectionChange()
        {
            if (!previewing)
                Bind();
        }
    }
}
