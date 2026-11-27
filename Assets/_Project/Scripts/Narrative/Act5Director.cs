using System.Collections;
using System.Collections.Generic;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.Narrative
{
    public enum Act5Beat
    {
        None = 0,
        Wake = 1,        // acorda presa na cela, no fundo do navio
        CellPuzzle = 2,  // a chave do lado de fora: chapa solta (barra), balde (arame), gancho, chave, cela
        Explore = 3,     // geradores 1 e 2: o Lenin ainda não caça, só sinais (passos, porta, cantarolar)
        Hunt = 4,        // do 3º gerador em diante: o Lenin caça de verdade
        Powered = 5,     // os 5 geradores: o portão abre
        Exit = 6         // ela sai para o convés: o gancho para o Ato 6
    }

    /// <summary>
    /// Maestro do Ato 5 — O Navio (2026-11-26). A Clear acorda presa num navio naufragado, resolve o puzzle da cela, liga
    /// 5 geradores espalhados (em qualquer ordem) e sai pelo portão que eles abrem. O Lenin (o estranho do Interlúdio,
    /// agora o captor) só deixa SINAIS nos dois primeiros geradores; no 3º ele aparece e caça. No fim, do convés, ela
    /// olha para trás e ele está parado no portão, olhando: "Continua...".
    /// </summary>
    public sealed class Act5Director : BeatDirector<Act5Beat>
    {
        [Header("Cena")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Act5Beat startBeat = Act5Beat.Wake;
        [SerializeField] private Door cellDoor;
        [SerializeField] private string cellLockId = "CellLock";
        [SerializeField] private GeneratorStation[] generators;
        [SerializeField] private ShipGate gate;
        [SerializeField] private Transform exitZone;
        [SerializeField] private float exitRadius = 2.5f;

        [Header("O Lenin")]
        [Tooltip("O captor (AntagonistAI). Começa desligado; acorda no gerador de número huntAtGenerator.")]
        [SerializeField] private GameObject lenin;
        [SerializeField] private Transform[] leninSpawnPoints;
        [SerializeField] private int huntAtGenerator = 3;
        [SerializeField] private AudioClip huntSting;
        [Tooltip("Onde ele fica parado, olhando, no fim (no vão do portão).")]
        [SerializeField] private Transform leninWatchPoint;

        [Header("Sinais (antes da caçada)")]
        [SerializeField] private AudioClip[] signalClips;
        [SerializeField] private Vector2 signalInterval = new Vector2(22f, 38f);

        [Header("Tempos")]
        [SerializeField] private float wakeBlack = 2.5f;
        [SerializeField] private float wakeFade = 3f;

        [Header("Celular (lanterna)")]
        [SerializeField] private GameObject phoneModel;
        [Tooltip("Passos da Clear no piso de aço do navio (trocam os do prefab).")]
        [SerializeField] private AudioClip[] shipFootsteps;
        [Tooltip("Clique ao ligar/desligar a lanterna (F sem alvo).")]
        [SerializeField] private AudioClip flashlightClick;
        private PhoneFlashlight flashlight;

        private CanvasGroup black;
        private TextMeshProUGUI endText;
        private int running;
        private Coroutine signalsRoutine;
        private AudioSource signalSource;

        private void Start()
        {
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            BuildOverlay();
            // a ÚNICA luz que ela carrega: a lanterna do celular, na mão direita
            if (Camera.main != null) flashlight = PhoneFlashlight.Create(Camera.main, phoneModel, playerController, flashlightClick);
            if (shipFootsteps != null && shipFootsteps.Length > 0 && playerController != null) playerController.SetFootstepSurface(shipFootsteps);
            // noite fechada: sem céu (a cena não tem skybox) o fundo é a cor da névoa, não o azul padrão da câmera
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor; }
            // o aviso 'Aperte espaço para levantar' (do Ato 4) vem ligado no prefab do Player: aqui ele não existe
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "StandUpPrompt") t.gameObject.SetActive(false);
            if (lenin != null)
            {
                // suspende ANTES de desligar: desligar mata a rotina da patrulha e ela ficaria marcada como ativa
                var ai0 = lenin.GetComponent<AntagonistAI>(); if (ai0 != null) ai0.SuspendForDirector();
                lenin.SetActive(false);
            }
            Act5Inventory.Clear();
            if (GetComponent<Act5InventoryUI>() == null) gameObject.AddComponent<Act5InventoryUI>();
            var go = new GameObject("Act5Signals"); signalSource = go.AddComponent<AudioSource>();
            signalSource.spatialBlend = 1f; signalSource.minDistance = 3f; signalSource.maxDistance = 45f; signalSource.rolloffMode = AudioRolloffMode.Linear;
            AdvanceToBeat(startBeat);
        }

        private void OnEnable()
        {
            GeneratorStation.Started += OnGeneratorStarted;
            PuzzleInteractable.Used += OnPuzzleUsed;
            Act5Inventory.Changed += OnInventoryChanged;
        }
        private void OnDisable()
        {
            GeneratorStation.Started -= OnGeneratorStarted;
            PuzzleInteractable.Used -= OnPuzzleUsed;
            Act5Inventory.Changed -= OnInventoryChanged;
        }

        protected override IEnumerator CreateBeatRoutine(Act5Beat beat) => beat switch
        {
            Act5Beat.Wake => BeatWake(),
            Act5Beat.CellPuzzle => BeatCellPuzzle(),
            Act5Beat.Explore => BeatExplore(),
            Act5Beat.Hunt => BeatHunt(),
            Act5Beat.Powered => BeatPowered(),
            Act5Beat.Exit => BeatExit(),
            _ => null
        };

        // ------------------------------------------------------------------ beats
        /// <summary>
        /// O DESPERTAR (2026-11-26): ela está caída de lado no chão da cela, atordoada. As pálpebras abrem com esforço,
        /// a visão inclinada balança, a lanterna do celular caída rente ao chão. Ela se apoia, senta e levanta, erguendo o
        /// celular na mão até a posição de sempre. Só então anda.
        /// </summary>
        private IEnumerator BeatWake()
        {
            playerController.CanMove = false;
            Transform cam = playerController.CameraHolder;
            float eye = playerController.EyeHeight;
            if (flashlight != null) flashlight.Raise = 0f;
            // caída de lado, a cabeça no aço, olhando para as grades
            Vector3 lyingPos = new Vector3(0.05f, 0.2f, 0f); Quaternion lyingRot = Quaternion.Euler(4f, -8f, 78f);
            Vector3 sitPos = new Vector3(0f, 0.85f, -0.1f); Quaternion sitRot = Quaternion.Euler(22f, 6f, 8f);
            Vector3 standPos = new Vector3(0f, eye, 0f); Quaternion standRot = Quaternion.Euler(6f, 0f, 0f);
            if (cam != null) { cam.localPosition = lyingPos; cam.localRotation = lyingRot; }
            SetLids(1f);
            black.alpha = 1f;
            yield return new WaitForSeconds(wakeBlack * 0.6f);
            black.alpha = 0f;                                         // daqui em diante, quem fecha a vista são as pálpebras

            bool swaying = true;
            IEnumerator Sway()
            {
                float ph = Random.value * 6f;
                while (swaying && cam != null)
                {
                    float t = Time.time * 0.9f + ph;
                    cam.localRotation = cam.localRotation * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 0.04f, Mathf.Sin(t * 0.5f) * 0.03f, Mathf.Sin(t) * 0.05f);
                    yield return null;
                }
            }
            IEnumerator Lid(float from, float to, float dur, bool falling)
            {
                for (float t = 0f; t < dur; t += Time.deltaTime)
                {
                    float u = Mathf.Clamp01(t / dur);
                    SetLids(Mathf.Lerp(from, to, falling ? 1f - (1f - u) * (1f - u) : Mathf.SmoothStep(0f, 1f, u)));
                    yield return null;
                }
                SetLids(to);
            }
            IEnumerator Move(Vector3 p0, Quaternion r0, Vector3 p1, Quaternion r1, float dur, float raise0, float raise1)
            {
                for (float t = 0f; t < dur && cam != null; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / dur);
                    cam.localPosition = Vector3.Lerp(p0, p1, k);
                    cam.localRotation = Quaternion.Slerp(r0, r1, k);
                    if (flashlight != null) flashlight.Raise = Mathf.Lerp(raise0, raise1, k);
                    yield return null;
                }
                if (cam != null) { cam.localPosition = p1; cam.localRotation = r1; }
                if (flashlight != null) flashlight.Raise = raise1;
            }

            // os olhos abrem com esforço: abre um pouco, pesa, fecha, abre de novo
            yield return Lid(1f, 0.55f, 1.6f, false);
            Coroutine sway = StartCoroutine(Sway());
            yield return new WaitForSeconds(0.7f);
            yield return Lid(0.55f, 1f, 0.5f, true);
            yield return new WaitForSeconds(0.6f);
            Think("...Onde... eu estou?");
            yield return Lid(1f, 0.25f, 1.8f, false);
            yield return new WaitForSeconds(1.2f);
            yield return Lid(0.25f, 0.7f, 0.35f, true);
            yield return Lid(0.7f, 0f, 1.2f, false);
            yield return WaitThought();
            swaying = false; if (sway != null) StopCoroutine(sway);

            // apoia o corpo e senta (zonza: a cabeça pende um pouco para a frente)
            yield return Move(cam != null ? cam.localPosition : lyingPos, cam != null ? cam.localRotation : lyingRot, sitPos, sitRot, 2.2f, 0f, 0.35f);
            Think("Isso balança... é um navio?");
            yield return new WaitForSeconds(1.4f);
            yield return Lid(0f, 0.6f, 0.25f, true);                  // uma tontura
            yield return Lid(0.6f, 0f, 0.6f, false);
            yield return WaitThought();

            // levanta, erguendo o celular
            yield return Move(sitPos, sitRot, standPos, standRot, 2.0f, 0.35f, 1f);
            playerController.SyncCameraState(6f, eye);
            SetLids(0f);
            playerController.CanMove = true;
            Think("Estou trancada. A chave... está ali fora.");
            yield return WaitThought();
            AdvanceToBeat(Act5Beat.CellPuzzle);
        }

        // pálpebras (0 = aberto, 1 = fechado), como no Ato 4
        private Canvas lidsCanvas; private RectTransform upperLid, lowerLid;
        private void SetLids(float closed)
        {
            closed = Mathf.Clamp01(closed);
            if (lidsCanvas == null)
            {
                if (closed <= 0f) return;
                var go = new GameObject("Act5Eyelids", typeof(Canvas));
                lidsCanvas = go.GetComponent<Canvas>();
                lidsCanvas.renderMode = RenderMode.ScreenSpaceOverlay; lidsCanvas.sortingOrder = 30000;
                RectTransform Bar(string n)
                {
                    var b = new GameObject(n, typeof(RectTransform), typeof(Image));
                    b.transform.SetParent(go.transform, false);
                    var img = b.GetComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
                    var rt = (RectTransform)b.transform; rt.offsetMin = rt.offsetMax = Vector2.zero;
                    return rt;
                }
                upperLid = Bar("UpperLid"); lowerLid = Bar("LowerLid");
            }
            lidsCanvas.gameObject.SetActive(closed > 0f);
            float half = 0.5f * closed + (closed >= 1f ? 0.02f : 0f);
            upperLid.anchorMin = new Vector2(0f, 1f - half); upperLid.anchorMax = new Vector2(1f, 1f);
            lowerLid.anchorMin = new Vector2(0f, 0f); lowerLid.anchorMax = new Vector2(1f, half);
        }

        private bool cellOpen;
        private IEnumerator BeatCellPuzzle()
        {
            while (!cellOpen) yield return null;
            yield return new WaitForSeconds(1.2f);
            Think("Preciso achar um jeito de sair desse navio.");
            yield return WaitThought();
            AdvanceToBeat(Act5Beat.Explore);
        }

        private IEnumerator BeatExplore()
        {
            if (signalsRoutine == null) signalsRoutine = StartCoroutine(Signals());
            if (running == 0) { Think("Tem um portão de carga... está sem energia. Geradores."); yield return WaitThought(); }
            while (running < huntAtGenerator) yield return null;
        }

        private IEnumerator BeatHunt()
        {
            if (signalsRoutine != null) { StopCoroutine(signalsRoutine); signalsRoutine = null; }
            WakeLenin();
            if (huntSting != null) JumpscareAudio.Play(huntSting);
            yield return new WaitForSeconds(0.6f);
            Think("Ele está aqui.");
            while (running < (generators != null ? generators.Length : 5)) yield return null;
        }

        private IEnumerator BeatPowered()
        {
            if (gate != null) { gate.SetPowered(running); gate.Open(); }
            Think("O portão! Agora!");
            if (exitZone == null) yield break;
            while (!DirectorUtility.IsWithinRadiusXZ(playerController.transform.position, exitZone.position, exitRadius)) yield return null;
            AdvanceToBeat(Act5Beat.Exit);
        }

        private IEnumerator BeatExit()
        {
            // o gancho para o Ato 6: ele não vem atrás. Fica parado no portão, olhando para ela.
            var ai = lenin != null ? lenin.GetComponent<AntagonistAI>() : null;
            if (ai != null) ai.SuspendForDirector();
            var agent = lenin != null ? lenin.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            if (agent != null) agent.enabled = false;
            if (lenin != null && leninWatchPoint != null)
            {
                lenin.SetActive(true);
                lenin.transform.SetPositionAndRotation(leninWatchPoint.position, leninWatchPoint.rotation);
                var anim = lenin.GetComponentInChildren<Animator>();
                if (anim != null) { anim.SetFloat("Speed", 0f); anim.SetBool("IsChasing", false); }
            }
            playerController.CanMove = false;
            yield return new WaitForSeconds(1.2f);
            // ela olha para trás
            Transform body = playerController.transform;
            Vector3 look = (leninWatchPoint != null ? leninWatchPoint.position : body.position - body.forward) - body.position; look.y = 0f;
            Quaternion from = body.rotation, to = Quaternion.LookRotation(look);
            for (float t = 0f; t < 1.6f; t += Time.deltaTime) { body.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / 1.6f)); yield return null; }
            playerController.SyncCameraState(-4f, playerController.EyeHeight);
            yield return new WaitForSeconds(0.8f);
            Think("...Por que ele não vem?");
            yield return WaitThought();
            yield return new WaitForSeconds(1.5f);
            yield return Fade(0f, 1f, 2.5f);
            endText.text = "Continua...";
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { endText.alpha = t / 1.5f; yield return null; }
            yield return new WaitForSeconds(3f);
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
        }

        // ------------------------------------------------------------------ eventos
        private void OnPuzzleUsed(PuzzleInteractable p)
        {
            if (p.Id == cellLockId && cellDoor != null)
            {
                cellDoor.IsLocked = false;
                cellDoor.Open();
                cellOpen = true;
            }
        }

        private void OnInventoryChanged()
        {
            // a barra + o arame viram um gancho
            if (Act5Inventory.Has("rod") && Act5Inventory.Has("wire"))
            {
                Act5Inventory.Remove("rod"); Act5Inventory.Remove("wire");
                Act5Inventory.Add("hook", "Gancho improvisado");
                Think("Se eu dobrar o arame na ponta da barra... vira um gancho.");
            }
        }

        private void OnGeneratorStarted(GeneratorStation g)
        {
            running = 0;
            if (generators != null) foreach (var x in generators) if (x != null && x.Running) running++;
            int total = generators != null ? generators.Length : 5;
            if (gate != null) gate.SetPowered(running);
            Debug.Log($"[Act5Director] Gerador {g.Id} ligado: {running}/{total}");
            if (running >= total) { AdvanceToBeat(Act5Beat.Powered); return; }
            if (running >= huntAtGenerator && CurrentBeat == Act5Beat.Explore) { AdvanceToBeat(Act5Beat.Hunt); return; }
            if (CurrentBeat == Act5Beat.Explore)
            {
                Think(running == 1 ? "Um... faltam quatro." : "Dois. Esse barulho todo...");
                PlaySignalFar();                                     // o navio responde
            }
            else Think($"{running}... faltam {total - running}.");
        }

        // ------------------------------------------------------------------ o Lenin
        private const float minSpawnDistance = 10f;

        private void WakeLenin()
        {
            if (lenin == null) return;
            Vector3 p = playerController.transform.position;
            Transform best = null; float bestScore = float.MinValue;
            if (leninSpawnPoints != null)
                foreach (var s in leninSpawnPoints)
                {
                    if (s == null) continue;
                    float d = Vector3.Distance(s.position, p);
                    bool seen = Seen(s.position);
                    // escondido, a ~16 m e NUNCA colado nela (< 10 m ele a pegaria antes de ela reagir)
                    float score = (seen ? -100f : 0f) - Mathf.Abs(d - 16f) - (d < minSpawnDistance ? 500f : 0f);
                    if (score > bestScore) { bestScore = score; best = s; }
                }
            if (best != null) lenin.transform.SetPositionAndRotation(best.position, best.rotation);
            lenin.SetActive(true);
            var agent = lenin.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) { agent.enabled = true; agent.Warp(lenin.transform.position); }
            // ele ouviu o 3º gerador: vem investigar onde ela está, depois procura e patrulha
            var ai = lenin.GetComponent<AntagonistAI>();
            if (ai != null) ai.InvestigateFromDirector(p);
            Debug.Log($"[Act5Director] O Lenin acordou em {(best != null ? best.name : "-")} — a caçada começou.");
        }

        private bool Seen(Vector3 floorPoint)
        {
            var cam = Camera.main; if (cam == null) return false;
            Vector3 c = floorPoint + Vector3.up * 1.2f;
            Vector3 vp = cam.WorldToViewportPoint(c);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
            return !Physics.Linecast(cam.transform.position, c, ~0, QueryTriggerInteraction.Ignore);
        }

        private IEnumerator Signals()
        {
            yield return new WaitForSeconds(8f);
            while (true)
            {
                PlaySignalFar();
                yield return new WaitForSeconds(Random.Range(signalInterval.x, signalInterval.y));
            }
        }

        /// <summary>Um sinal dele, LONGE: passos, uma porta batendo, o cantarolar — de um ponto a 15–25 m dela.</summary>
        private void PlaySignalFar()
        {
            if (signalClips == null || signalClips.Length == 0 || signalSource == null) return;
            Vector3 p = playerController.transform.position;
            Vector3 at = p + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(15f, 25f);
            if (leninSpawnPoints != null && leninSpawnPoints.Length > 0)
            {
                var far = leninSpawnPoints[Random.Range(0, leninSpawnPoints.Length)];
                if (far != null && Vector3.Distance(far.position, p) > 10f) at = far.position;
            }
            signalSource.transform.position = at + Vector3.up * 1.5f;
            var clip = signalClips[Random.Range(0, signalClips.Length)];
            if (clip != null) signalSource.PlayOneShot(clip, 1f);
        }

        // ------------------------------------------------------------------ utilidades
        private static void Think(string text) { if (ThoughtSystem.Instance != null) ThoughtSystem.Instance.Show(text); }
        private static IEnumerator WaitThought()
        {
            yield return null;
            while (ThoughtSystem.Instance != null && ThoughtSystem.Instance.IsShowing) yield return null;
        }

        private void BuildOverlay()
        {
            var go = new GameObject("Act5Overlay", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 900;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
            var bg = new GameObject("Black", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            bg.transform.SetParent(go.transform, false);
            var rt = (RectTransform)bg.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = bg.GetComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
            black = bg.GetComponent<CanvasGroup>(); black.alpha = 0f; black.blocksRaycasts = false;
            var tx = new GameObject("EndText", typeof(RectTransform), typeof(TextMeshProUGUI));
            tx.transform.SetParent(go.transform, false);
            endText = tx.GetComponent<TextMeshProUGUI>();
            var trt = endText.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            endText.alignment = TextAlignmentOptions.Center; endText.fontSize = 64; endText.color = new Color(0.85f, 0.82f, 0.75f); endText.alpha = 0f; endText.text = "";
        }

        private IEnumerator Fade(float a, float b, float d)
        {
            for (float t = 0f; t < d; t += Time.deltaTime) { black.alpha = Mathf.Lerp(a, b, t / d); yield return null; }
            black.alpha = b;
        }
    }
}
