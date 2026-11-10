using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ALINHA A ESTRUTURA DO APARTAMENTO (Paredes, Chão, Teto) — o espaço onde o player e o
    /// antagonista andam nos Atos 3 e 4. As peças são ProBuilder com os VÉRTICES
    /// desencontrados: frestas entre peças que deveriam se encostar, pedaços da mesma parede
    /// que entram um no outro (as faces coincidem e piscam — z-fight), pedaços da mesma
    /// parede em planos ou espessuras diferentes (o "degrau" na emenda).
    ///
    /// A PRIMEIRA VERSÃO comparava as peças DUAS A DUAS, e isso não converge: um pedaço
    /// entre dois vizinhos desencontrados entre si se alinhava a um, depois ao outro; aparar
    /// uma sobreposição abria uma fresta que a regra seguinte esticava de volta. Reanalisar
    /// depois de aplicar sempre achava "novas" correções. Esta versão raciocina sobre o
    /// CONJUNTO e é idempotente: aplicar e analisar de novo dá zero (a janela confere).
    ///
    /// AS ETAPAS, em ordem, todas simuladas antes de tocar em qualquer malha:
    /// 0. ESQUADRO — peça cujas faces externas não estão num plano só (a Corredor/Parede
    ///    #3421 tem as pontas inclinadas 4,6 mm) volta a ser caixa: todo vértice a até
    ///    <see cref="squareTolerance"/> de uma face vai para ela. Sem isso, mover a face
    ///    arrasta só a quina que está no extremo e a peça ENTORTA mais a cada aplicação —
    ///    o que as versões anteriores desta ferramenta faziam.
    /// 1. LINHAS — paredes paralelas cujo centro cai a menos de <see cref="lineTolerance"/>
    ///    formam UMA linha de parede, e todas recebem os mesmos dois planos: os da maior
    ///    peça de espessura padrão da linha. Se a linha só tem peças finas, ela engorda até a
    ///    espessura padrão em torno do centro comum (a parede da frente da Sala). Uma linha
    ///    só de peças mais grossas fica com os planos da maior delas.
    /// 2. NÍVEL — topo dos chãos e base dos tetos no nível comum (mediana); parede que não
    ///    chega ao chão/teto desce/sobe até entrar 1 cm nele.
    /// 3. PLANOS GLOBAIS — a estrutura como um todo: em cada eixo, todas as faces de todas
    ///    as peças (qualquer tipo ou tamanho, encostadas ou não) a menos de
    ///    <see cref="planeTolerance"/> umas das outras viram um único plano — o da face de
    ///    parede mais visível do grupo. Em Y, só as alturas internas (vergas, peitoris).
    /// 4. PONTAS — cada face lateral (ponta de parede, borda de chão/teto):
    ///    • se entra numa peça COPLANAR (mesma parede/laje), é aparada até a face dela —
    ///      "Emenda", fim do z-fight;
    ///    • se já está COBERTA — as peças que atravessam aquele plano tapam ≥95% da parte
    ///      visível da face (entre o chão e o teto) — está fechada e não mexe;
    ///    • senão, vai até o plano mais próximo à frente (≤ <see cref="maxGap"/>) em que
    ///      ficaria coberta — "Fresta". Vão maior que isso é porta/janela, não é tocado.
    ///    Repete até nada mudar.
    /// As quatro etapas rodam em VOLTAS até nenhuma mudar mais nada (uma afeta a outra).
    ///
    /// COMO APLICA: move só os vértices da face (em espaço de mundo) até o alvo final da
    /// simulação; nunca estica pela escala do transform. Peça corrigida com escala ≠ 1 (e sem
    /// filhos) tem a escala "assada" nos vértices. Tudo num único Undo.
    /// </summary>
    public sealed class ApartmentStructureAlignWindow : EditorWindow
    {
        private enum Kind { Floor, Wall, Ceiling }

        private sealed class Piece
        {
            public ProBuilderMesh Mesh;
            public Kind Kind;
            public string Path;
            public readonly float[] Min = new float[3], Max = new float[3];   // simulado
            public readonly float[] Min0 = new float[3], Max0 = new float[3]; // original

            public float Face(int axis, bool maxSide) => maxSide ? Max[axis] : Min[axis];
            public float Face0(int axis, bool maxSide) => maxSide ? Max0[axis] : Min0[axis];
            public float Extent(int axis) => Max[axis] - Min[axis];
            public float Center(int axis) => (Min[axis] + Max[axis]) * 0.5f;

            public int ThinAxis
            {
                get
                {
                    float x = Extent(0), y = Extent(1), z = Extent(2);
                    return x <= y && x <= z ? 0 : y <= z ? 1 : 2;
                }
            }

            public float Area
            {
                get
                {
                    int t = ThinAxis;
                    float a = 1f;
                    for (int i = 0; i < 3; i++)
                        if (i != t) a *= Extent(i);
                    return a;
                }
            }
        }

        private sealed class Fix
        {
            public Piece Piece;
            public int Axis;
            public bool MaxSide;
            public float From, Target;
            public string Rule;
            public string Detail;    // Esquadro (Axis = -1): o que muda
            public float Tolerance;  // Esquadro: distância máxima levada à face
            public bool Enabled = true;
        }

        private static readonly string[] AxisName = { "X", "Y", "Z" };

        [SerializeField] private string rootName = "_Environment";
        [SerializeField] private float maxGap = 0.2f;
        [SerializeField] private float lineTolerance = 0.06f;
        [SerializeField] private float levelTolerance = 0.06f;
        [SerializeField] private bool stepSquare = true;
        [SerializeField] private float squareTolerance = 0.006f;
        [SerializeField] private bool stepLines = true;
        [SerializeField] private bool stepLevels = true;
        [SerializeField] private bool stepPlanes = true;
        [SerializeField] private bool stepEnds = true;
        [SerializeField] private float planeTolerance = 0.035f;

        private const float MinDelta = 0.002f;   // abaixo disso é ruído de float
        private const float MinContact = 0.02f;  // contato mínimo entre vizinhos
        private const float SameThickness = 0.01f;
        private const float FaceEpsilon = 0.003f;
        private const float SealedCoverage = 0.95f;
        private const int CoverageSamples = 16;
        private const float Embed = 0.01f;       // quanto a parede entra no chão/teto
        private const int MaxPasses = 30;
        private const int MaxOuterLoops = 12;

        private readonly List<Fix> fixes = new List<Fix>();
        private readonly List<string> skipped = new List<string>();
        private List<ProBuilderMesh> scope = new List<ProBuilderMesh>();
        private float floorLevel, ceilingLevel, wallThickness;
        private int passesUsed, outerLoops;
        private bool analyzed, converged = true;
        private string lastApplyReport;
        private Vector2 scroll;

        [MenuItem("Tools/The Delivery/Apartamento - Alinhar estrutura (paredes, chão, teto)")]
        private static void Open() => GetWindow<ApartmentStructureAlignWindow>("Alinhar estrutura");

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RebuildScopeMeshes;
            SceneView.duringSceneGui += DrawSceneOverlay;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildScopeMeshes;
            SceneView.duringSceneGui -= DrawSceneOverlay;
        }

        // --- UI ---------------------------------------------------------------------

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Alinha as peças Parede / Chao / Teto sob o root (a estrutura do apartamento, Atos 3 e 4). " +
                "Analise, revise e aplique — um único Undo. Depois de aplicar, a janela reanalisa: tem que dar 0.",
                MessageType.None);

            rootName = EditorGUILayout.TextField("Root da estrutura", rootName);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Etapas", EditorStyles.boldLabel);
            stepSquare = EditorGUILayout.ToggleLeft("0. Esquadro — peça torta (ponta inclinada) volta a ser caixa", stepSquare);
            stepLines = EditorGUILayout.ToggleLeft("1. Linhas — pedaços da mesma parede no mesmo plano e espessura", stepLines);
            stepLevels = EditorGUILayout.ToggleLeft("2. Nível — chão, teto, pé e topo das paredes", stepLevels);
            stepPlanes = EditorGUILayout.ToggleLeft("3. Planos globais — a estrutura toda, qualquer tamanho de peça", stepPlanes);
            stepEnds = EditorGUILayout.ToggleLeft("4. Pontas — emendas (z-fight) e frestas", stepEnds);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Tolerâncias (m)", EditorStyles.boldLabel);
            maxGap = EditorGUILayout.Slider(new GUIContent("Fresta máx.",
                "Acima disso é vão intencional (porta, janela) e não é tocado."), maxGap, 0.01f, 0.5f);
            lineTolerance = EditorGUILayout.Slider(new GUIContent("Desvio de linha máx.",
                "Paredes paralelas com centros mais próximos que isso são a MESMA parede."), lineTolerance, 0.005f, 0.12f);
            levelTolerance = EditorGUILayout.Slider("Desvio de nível máx.", levelTolerance, 0.005f, 0.2f);
            squareTolerance = EditorGUILayout.Slider(new GUIContent("Esquadro máx.",
                "Vértice a até esta distância de uma face externa é levado a ela. Na cena original o " +
                "maior desvio é 4,6 mm; arestas internas legítimas ficam a ≥ 9 mm."), squareTolerance, 0.001f, 0.05f);
            planeTolerance = EditorGUILayout.Slider(new GUIContent("Plano global máx.",
                "Faces mais próximas que isso (em qualquer peça) viram um único plano. Mantenha abaixo " +
                "da menor distância INTENCIONAL entre faces (ex.: espessura da parede, 0,15)."), planeTolerance, 0.005f, 0.08f);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analisar", GUILayout.Height(28)))
                    Analyze();

                using (new EditorGUI.DisabledScope(!analyzed || fixes.All(f => !f.Enabled)))
                {
                    if (GUILayout.Button($"Aplicar ({fixes.Count(f => f.Enabled)})", GUILayout.Height(28)))
                        Apply();
                }

                if (GUILayout.Button("Refazer NavMesh", GUILayout.Height(28)))
                    RebakeNavMesh();
            }

            if (!string.IsNullOrEmpty(lastApplyReport))
                EditorGUILayout.HelpBox(lastApplyReport, fixes.Count == 0 ? MessageType.Info : MessageType.Warning);

            if (!analyzed)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                $"{scope.Count} peças · parede {wallThickness:0.000} · chão {floorLevel:0.000} · teto {ceilingLevel:0.000} · " +
                $"{fixes.Count} correções em {fixes.Select(f => f.Piece).Distinct().Count()} peças", EditorStyles.miniLabel);

            if (!converged)
                EditorGUILayout.HelpBox($"A simulação não estabilizou em {MaxOuterLoops} voltas — algumas faces disputam alvo. " +
                                        "Reduza a fresta máx. ou o plano global máx. e analise de novo.", MessageType.Error);

            if (skipped.Count > 0)
                EditorGUILayout.HelpBox("Ignoradas (rotação fora de 90°):\n" + string.Join("\n", skipped), MessageType.Warning);

            if (fixes.Count == 0)
            {
                EditorGUILayout.HelpBox("Nada a corrigir: a estrutura está alinhada.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox("As correções dependem umas das outras (uma ponta vai até a parede já alinhada). " +
                                    "Desmarcar itens pode deixar a reanálise com sobras.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Marcar todas", EditorStyles.miniButtonLeft))
                    fixes.ForEach(f => f.Enabled = true);
                if (GUILayout.Button("Desmarcar todas", EditorStyles.miniButtonRight))
                    fixes.ForEach(f => f.Enabled = false);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Fix fix in fixes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    fix.Enabled = EditorGUILayout.Toggle(fix.Enabled, GUILayout.Width(16));
                    EditorGUILayout.LabelField(fix.Rule, GUILayout.Width(70));
                    if (GUILayout.Button(fix.Piece.Path, EditorStyles.linkLabel, GUILayout.MinWidth(140)))
                    {
                        Selection.activeGameObject = fix.Piece.Mesh.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }

                    float d = fix.Target - fix.From;
                    string what = fix.Axis < 0
                        ? fix.Detail
                        : $"{AxisName[fix.Axis]}{(fix.MaxSide ? "máx" : "mín")}  {fix.From:0.000} → {fix.Target:0.000}  ({d * 100f:+0.0;-0.0} cm)";
                    EditorGUILayout.LabelField(what, EditorStyles.miniLabel, GUILayout.MinWidth(180));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // --- Análise ----------------------------------------------------------------

        private void Analyze()
        {
            fixes.Clear();
            skipped.Clear();
            analyzed = false;
            converged = true;

            GameObject root = FindRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("Alinhar estrutura",
                    $"\"{rootName}\" não encontrado na cena aberta.\n\nAbra Assets/Scenes/Apartamento.unity.", "Ok");
                return;
            }

            var pieces = new List<Piece>();
            scope = new List<ProBuilderMesh>();
            foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(includeInactive: false))
            {
                if (!TryKind(pb.gameObject.name, out Kind kind))
                    continue;

                string path = PathOf(pb.transform, root.transform);
                if (!IsAxisAligned(pb.transform))
                {
                    skipped.Add(path);
                    continue;
                }

                var piece = new Piece { Mesh = pb, Kind = kind, Path = path };
                WorldBounds(pb, piece.Min, piece.Max);
                piece.Min.CopyTo(piece.Min0, 0);
                piece.Max.CopyTo(piece.Max0, 0);
                pieces.Add(piece);
                scope.Add(pb);
            }

            var walls = pieces.Where(p => p.Kind == Kind.Wall).ToList();
            wallThickness = Median(walls.Select(p => p.Extent(p.ThinAxis)));
            floorLevel = Median(pieces.Where(p => p.Kind == Kind.Floor).Select(p => p.Max[1]));
            ceilingLevel = Median(pieces.Where(p => p.Kind == Kind.Ceiling).Select(p => p.Min[1]));

            var rules = new Dictionary<(Piece, int, bool), string>();

            int changes = 0;

            void Set(Piece p, int axis, bool maxSide, float value, string rule)
            {
                if (Mathf.Abs(p.Face(axis, maxSide) - value) < 1e-6f)
                    return;
                if (maxSide) p.Max[axis] = value; else p.Min[axis] = value;
                if (!rules.ContainsKey((p, axis, maxSide)))
                    rules[(p, axis, maxSide)] = rule;
                changes++;
            }

            // As etapas se influenciam (um plano global muda a espessura que a etapa de
            // linhas normaliza; uma linha nova muda o plano de referência). Como tudo é
            // simulado, a sequência inteira roda até NADA mudar — é isso que faz aplicar e
            // reanalisar dar zero.
            converged = false;
            for (outerLoops = 1; outerLoops <= MaxOuterLoops; outerLoops++)
            {
                changes = 0;
                bool endsStable = true;
                if (stepLines) AlignLines(walls, Set);
                if (stepLevels) AlignLevels(pieces, Set);
                if (stepPlanes) AlignPlanes(pieces, Set);
                if (stepEnds) endsStable = AlignEnds(pieces, Set);

                if (changes == 0 && endsStable)
                {
                    converged = true;
                    break;
                }
            }

            // Esquadro: peças cujas faces externas não estão num plano só (ponta inclinada,
            // quina solta). Não muda a caixa da peça — só a forma dentro dela.
            if (stepSquare)
            {
                foreach (Piece p in pieces)
                {
                    Vector3[] world = p.Mesh.positions.Select(v => p.Mesh.transform.TransformPoint(v)).ToArray();
                    var (moved, worst) = SquareUp(world, squareTolerance);
                    if (moved > 0)
                        fixes.Add(new Fix
                        {
                            Piece = p, Axis = -1, Rule = "Esquadro", Tolerance = squareTolerance,
                            Detail = $"{moved} coordenadas de vértice até a face (maior desvio {worst * 1000f:0.0} mm)"
                        });
                }
            }

            // O resultado é a DIFERENÇA líquida por face entre o simulado e o original.
            foreach (Piece p in pieces)
                for (int a = 0; a < 3; a++)
                    foreach (bool mx in new[] { false, true })
                    {
                        float from = p.Face0(a, mx), to = p.Face(a, mx);
                        if (Mathf.Abs(from - to) >= MinDelta)
                            fixes.Add(new Fix
                            {
                                Piece = p, Axis = a, MaxSide = mx, From = from, Target = to,
                                Rule = rules.TryGetValue((p, a, mx), out string r) ? r : "?"
                            });
                    }

            analyzed = true;
            SceneView.RepaintAll();
            Debug.Log($"[AlinharEstrutura] {pieces.Count} peças, {fixes.Count} correções em " +
                      $"{fixes.Select(f => f.Piece).Distinct().Count()} peças ({outerLoops} voltas" +
                      $"{(converged ? "" : ", NÃO convergiu")}).");
        }

        private delegate void Setter(Piece p, int axis, bool maxSide, float value, string rule);

        /// <summary>Etapa 1: cada linha de parede ganha um único par de planos.</summary>
        private void AlignLines(List<Piece> walls, Setter set)
        {
            foreach (int t in new[] { 0, 2 })
            {
                var groups = new List<List<Piece>>();
                foreach (Piece w in walls.Where(w => w.ThinAxis == t).OrderBy(w => w.Center(t)))
                {
                    if (groups.Count > 0 && w.Center(t) - groups[^1].Average(o => o.Center(t)) <= lineTolerance)
                        groups[^1].Add(w);
                    else
                        groups.Add(new List<Piece> { w });
                }

                foreach (List<Piece> line in groups)
                {
                    float min, max;
                    Piece standard = line
                        .Where(w => Mathf.Abs(w.Extent(t) - wallThickness) <= SameThickness)
                        .OrderByDescending(w => w.Area)
                        .FirstOrDefault();

                    if (standard != null)
                    {
                        min = standard.Min[t];
                        max = standard.Max[t];
                    }
                    else if (line.All(w => w.Extent(t) < wallThickness))
                    {
                        float c = line.Average(w => w.Center(t));
                        min = c - wallThickness * 0.5f;
                        max = c + wallThickness * 0.5f;
                    }
                    else
                    {
                        Piece biggest = line.OrderByDescending(w => w.Area).First();
                        min = biggest.Min[t];
                        max = biggest.Max[t];
                    }

                    string rule = line.Count > 1 ? "Linha" : "Espessura";
                    foreach (Piece w in line)
                    {
                        if (Mathf.Abs(w.Min[t] - min) >= MinDelta) set(w, t, false, min, rule);
                        if (Mathf.Abs(w.Max[t] - max) >= MinDelta) set(w, t, true, max, rule);
                    }
                }
            }
        }

        /// <summary>Etapa 2: níveis de chão e teto; paredes entram 1 cm neles.</summary>
        private void AlignLevels(List<Piece> pieces, Setter set)
        {
            foreach (Piece p in pieces)
            {
                switch (p.Kind)
                {
                    case Kind.Floor:
                    {
                        float d = floorLevel - p.Max[1];
                        if (Mathf.Abs(d) >= MinDelta && Mathf.Abs(d) <= levelTolerance)
                            set(p, 1, true, floorLevel, "Nível");
                        break;
                    }
                    case Kind.Ceiling:
                    {
                        float d = ceilingLevel - p.Min[1];
                        if (Mathf.Abs(d) >= MinDelta && Mathf.Abs(d) <= levelTolerance)
                            set(p, 1, false, ceilingLevel, "Nível");
                        break;
                    }
                    case Kind.Wall:
                    {
                        float above = p.Min[1] - (floorLevel - 0.003f);
                        if (above > 0f && above <= levelTolerance)
                            set(p, 1, false, floorLevel - Embed, "Nível");
                        float below = (ceilingLevel + 0.003f) - p.Max[1];
                        if (below > 0f && below <= levelTolerance)
                            set(p, 1, true, ceilingLevel + Embed, "Nível");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Etapa 3: PLANOS GLOBAIS — a estrutura como um todo. Em cada eixo, TODAS as faces
        /// (parede, chão, teto; de qualquer tamanho, encostadas ou não) que caem a menos de
        /// <see cref="planeTolerance"/> umas das outras viram um único plano. É o que as
        /// outras etapas não pegam: elas exigem mesmo tipo, mesma espessura ou contato, e
        /// por isso deixavam de fora peças de tamanhos diferentes (parede de 2,6 m ao lado
        /// de uma de 11,4 m, piso de 0,6 cm ao lado de um de 10 cm, teto de 8,9 m).
        ///
        /// O plano escolhido é o da face mais VISÍVEL do grupo (ver <see cref="Weight"/>):
        /// parede manda sobre chão, chão sobre teto. Em Y só entram as alturas de dentro do
        /// ambiente (vergas de porta, peitoris de janela) — chão e teto são da etapa de nível,
        /// e o que fica abaixo do piso/acima do forro não aparece.
        /// </summary>
        private void AlignPlanes(List<Piece> pieces, Setter set)
        {
            foreach (int axis in new[] { 0, 2, 1 })
            {
                var faces = new List<(Piece piece, bool maxSide, float value, float weight)>();
                foreach (Piece p in pieces)
                {
                    foreach (bool mx in new[] { false, true })
                    {
                        float v = p.Face(axis, mx);
                        if (axis == 1 && (p.Kind != Kind.Wall || v <= floorLevel + 0.05f || v >= ceilingLevel - 0.05f))
                            continue;
                        faces.Add((p, mx, v, Weight(p, axis)));
                    }
                }

                faces.Sort((a, b) => a.value.CompareTo(b.value));

                // Grupos de ENVERGADURA limitada (do primeiro ao último ≤ tolerância): sem
                // isso, uma escada de faces 2 cm uma da outra viraria um plano só.
                for (int start = 0; start < faces.Count;)
                {
                    int end = start;
                    while (end + 1 < faces.Count && faces[end + 1].value - faces[start].value <= planeTolerance)
                        end++;

                    var group = faces.GetRange(start, end - start + 1);
                    start = end + 1;
                    if (group.Max(f => f.value) - group.Min(f => f.value) < MinDelta)
                        continue;

                    // O valor que MAIS pesa (somando faces que já coincidem) é o plano.
                    float plane = group
                        .GroupBy(f => Mathf.Round(f.value / 0.0005f))
                        .OrderByDescending(g => g.Sum(f => f.weight))
                        .First()
                        .OrderByDescending(f => f.weight)
                        .First().value;

                    foreach (var f in group)
                    {
                        if (Mathf.Abs(f.value - plane) < MinDelta)
                            continue;
                        // Uma peça não pode ter as duas faces no mesmo plano (espessura zero).
                        if (group.Any(o => o.piece == f.piece && o.maxSide != f.maxSide))
                            continue;
                        set(f.piece, axis, f.maxSide, plane, "Plano");
                    }
                }
            }
        }

        /// <summary>
        /// Peso de uma face na escolha do plano: parede &gt; chão &gt; teto; dentro do tipo, a
        /// área VISÍVEL (recortada entre o chão e o teto) — um teto de 8,9 m de altura não
        /// pesa mais que uma parede por causa do volume escondido acima do forro.
        /// </summary>
        private float Weight(Piece p, int axis)
        {
            if (axis == 1)
                return p.Extent(0) * p.Extent(2);

            int j = 2 - axis;
            float height = Mathf.Max(0f, Mathf.Min(p.Max[1], ceilingLevel) - Mathf.Max(p.Min[1], floorLevel));
            switch (p.Kind)
            {
                case Kind.Wall: return 1000f + p.Extent(j) * height;
                case Kind.Floor: return 10f + p.Extent(j);
                default: return 1f + p.Extent(j);
            }
        }

        /// <summary>Etapa 4: emendas coplanares e frestas, repetindo até estabilizar.</summary>
        private bool AlignEnds(List<Piece> pieces, Setter set)
        {
            for (passesUsed = 0; passesUsed < MaxPasses; passesUsed++)
            {
                int moved = 0;
                foreach (Piece p in pieces)
                {
                    foreach (int i in new[] { 0, 2 })
                    {
                        if (p.Kind == Kind.Wall && i == p.ThinAxis)
                            continue; // planos da parede são da etapa 1

                        foreach (bool mx in new[] { false, true })
                        {
                            if (!VisibleBand(p, out float y0, out float y1))
                                continue;

                            float f = p.Face(i, mx);

                            // (a) entra numa peça coplanar: apara até a face dela (z-fight).
                            if (TrimToCoplanar(p, i, mx, f, pieces, out float trim))
                            {
                                set(p, i, mx, trim, "Emenda");
                                moved++;
                                continue;
                            }

                            // (b) coberta por quem atravessa o plano: fechada.
                            if (Coverage(p, i, f, y0, y1, pieces) >= SealedCoverage)
                                continue;

                            // (c) o plano mais próximo à frente em que ficaria coberta.
                            float s = mx ? 1f : -1f;
                            IEnumerable<float> candidates = pieces
                                .Where(q => q != p)
                                .Select(q => mx ? q.Min[i] : q.Max[i])
                                .Where(t => (t - f) * s > MinDelta && (t - f) * s <= maxGap)
                                .Distinct()
                                .OrderBy(t => Mathf.Abs(t - f));

                            foreach (float t in candidates)
                            {
                                if (Coverage(p, i, t, y0, y1, pieces) < SealedCoverage)
                                    continue;
                                set(p, i, mx, t, "Fresta");
                                moved++;
                                break;
                            }
                        }
                    }
                }

                if (moved == 0)
                    return true;
            }

            return false;
        }

        private bool TrimToCoplanar(Piece p, int i, bool mx, float f, List<Piece> pieces, out float target)
        {
            target = 0f;
            bool found = false;
            int j = 2 - i;
            foreach (Piece q in pieces)
            {
                if (q == p || !Coplanar(p, q) || Overlap(p, q, j) < MinContact)
                    continue;

                // q começa DENTRO de p, perto da ponta, e segue além dela.
                bool inside = mx
                    ? q.Min[i] < f - MinDelta && q.Min[i] > p.Min[i] + MinDelta && q.Max[i] > f + MinDelta
                    : q.Max[i] > f + MinDelta && q.Max[i] < p.Max[i] - MinDelta && q.Min[i] < f - MinDelta;
                float t = mx ? q.Min[i] : q.Max[i];
                if (!inside || Mathf.Abs(t - f) > maxGap)
                    continue;

                if (!found || Mathf.Abs(t - f) < Mathf.Abs(target - f))
                {
                    target = t;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Faixa vertical VISÍVEL da face: o trecho da peça dentro do ambiente. Chão = uma
        /// lâmina logo abaixo do nível (a borda do piso); teto = logo acima da base.
        /// </summary>
        private bool VisibleBand(Piece p, out float y0, out float y1)
        {
            switch (p.Kind)
            {
                case Kind.Floor:
                    y0 = floorLevel - 0.004f; y1 = floorLevel - 0.001f;
                    return true;
                case Kind.Ceiling:
                    y0 = ceilingLevel + 0.0005f; y1 = ceilingLevel + 0.002f;
                    return true;
                default:
                    y0 = Mathf.Max(p.Min[1], floorLevel) + 0.005f;
                    y1 = Mathf.Min(p.Max[1], ceilingLevel) - 0.005f;
                    return y1 > y0;
            }
        }

        /// <summary>
        /// Fração da face (plano i = f; largura j da peça × faixa y0..y1) tapada por peças que
        /// ATRAVESSAM esse plano. Amostragem em grade: simples e robusta a várias peças
        /// cobrindo pedaços da mesma borda (ex.: paredes dos dois lados de uma porta).
        /// </summary>
        private static float Coverage(Piece p, int i, float f, float y0, float y1, List<Piece> pieces)
        {
            int j = 2 - i;
            float j0 = p.Min[j] + 0.005f, j1 = p.Max[j] - 0.005f;
            if (j1 <= j0)
                return 1f;

            var covering = pieces.Where(q => q != p &&
                                             q.Min[i] <= f + MinDelta && q.Max[i] >= f - MinDelta &&
                                             q.Max[j] > j0 && q.Min[j] < j1 &&
                                             q.Max[1] > y0 && q.Min[1] < y1).ToList();
            if (covering.Count == 0)
                return 0f;

            int hit = 0;
            for (int a = 0; a < CoverageSamples; a++)
            {
                float jj = j0 + (j1 - j0) * (a + 0.5f) / CoverageSamples;
                for (int b = 0; b < CoverageSamples; b++)
                {
                    float yy = y0 + (y1 - y0) * (b + 0.5f) / CoverageSamples;
                    foreach (Piece q in covering)
                    {
                        if (jj >= q.Min[j] && jj <= q.Max[j] && yy >= q.Min[1] && yy <= q.Max[1])
                        {
                            hit++;
                            break;
                        }
                    }
                }
            }

            return hit / (float)(CoverageSamples * CoverageSamples);
        }

        /// <summary>Mesma parede/laje: mesmo tipo, mesmo eixo fino e o plano VISÍVEL em comum.</summary>
        private static bool Coplanar(Piece a, Piece b)
        {
            if (a.Kind != b.Kind || a.ThinAxis != b.ThinAxis)
                return false;

            switch (a.Kind)
            {
                case Kind.Floor: return Mathf.Abs(a.Max[1] - b.Max[1]) < SameThickness;
                case Kind.Ceiling: return Mathf.Abs(a.Min[1] - b.Min[1]) < SameThickness;
                default:
                    int t = a.ThinAxis;
                    return Mathf.Abs(a.Min[t] - b.Min[t]) < SameThickness ||
                           Mathf.Abs(a.Max[t] - b.Max[t]) < SameThickness;
            }
        }

        // --- Aplicação --------------------------------------------------------------

        private void Apply()
        {
            List<Fix> todo = fixes.Where(f => f.Enabled && f.Piece.Mesh != null).ToList();
            var meshes = todo.Select(f => f.Piece.Mesh).Distinct().ToList();

            var undoTargets = new List<Object>();
            foreach (ProBuilderMesh pb in meshes)
            {
                undoTargets.Add(pb);
                undoTargets.Add(pb.transform); // a escala pode ser assada
                if (pb.TryGetComponent(out MeshFilter mf)) undoTargets.Add(mf);
                if (pb.TryGetComponent(out MeshCollider mc)) undoTargets.Add(mc);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(undoTargets.ToArray(), "Alinhar estrutura do apartamento");

            foreach (IGrouping<ProBuilderMesh, Fix> perMesh in todo.GroupBy(f => f.Piece.Mesh))
                ApplyToMesh(perMesh.Key, perMesh.ToList());

            foreach (ProBuilderMesh pb in meshes)
                Rebuild(pb);

            Undo.CollapseUndoOperations(group);
            if (meshes.Count > 0)
                EditorSceneManager.MarkSceneDirty(meshes[0].gameObject.scene);

            // A PROVA da convergência: reanalisa na hora e diz o que sobrou.
            int applied = todo.Count;
            Analyze();
            lastApplyReport = fixes.Count == 0
                ? $"{applied} correções aplicadas em {meshes.Count} peças. Reanálise: 0 — estrutura alinhada. " +
                  "Refaça o NavMesh e SALVE a cena (Ctrl+S)."
                : $"{applied} correções aplicadas em {meshes.Count} peças. Reanálise ainda achou {fixes.Count} — " +
                  "provavelmente de itens desmarcados. Aplique de novo ou revise.";
            Debug.Log($"[AlinharEstrutura] {lastApplyReport}");
        }

        /// <summary>
        /// Leva cada face à coordenada-alvo. As faces são identificadas NA MALHA ORIGINAL
        /// (antes de qualquer movimento desta peça), para que mover a face mínima não mude
        /// quais vértices pertencem à máxima.
        /// </summary>
        private static void ApplyToMesh(ProBuilderMesh pb, List<Fix> meshFixes)
        {
            Transform t = pb.transform;
            Vector3[] local = pb.positions.ToArray();
            var world = new Vector3[local.Length];
            for (int k = 0; k < local.Length; k++)
                world[k] = t.TransformPoint(local[k]);

            // ESQUADRO primeiro: a face tem que estar num plano só antes de ser movida —
            // senão só a quina que está no extremo anda, e a peça entorta mais.
            Fix square = meshFixes.FirstOrDefault(f => f.Axis < 0);
            if (square != null)
                SquareUp(world, square.Tolerance);

            var moves = new List<(int vertex, int axis, float value)>();
            foreach (Fix fix in meshFixes)
            {
                if (fix.Axis < 0)
                    continue;
                int a = fix.Axis;
                float face = fix.MaxSide ? world.Max(w => w[a]) : world.Min(w => w[a]);
                for (int k = 0; k < world.Length; k++)
                {
                    if (Mathf.Abs(world[k][a] - face) <= FaceEpsilon)
                        moves.Add((k, a, fix.Target));
                }
            }

            foreach (var (vertex, axis, value) in moves)
                world[vertex][axis] = value;

            // Escala ≠ 1 assada nos vértices: mesma forma no mundo, transform limpo.
            if (t.childCount == 0 && (t.localScale - Vector3.one).sqrMagnitude > 1e-8f)
                t.localScale = Vector3.one;

            for (int k = 0; k < local.Length; k++)
                local[k] = t.InverseTransformPoint(world[k]);

            pb.positions = local;
        }

        /// <summary>
        /// Leva à face externa toda coordenada que está a até <paramref name="tolerance"/>
        /// dela (em cada eixo, em espaço de mundo). Desfaz ponta inclinada e face "em degrau"
        /// sem tocar em arestas internas legítimas, que ficam longe das faces.
        /// Devolve quantos vértices mudaram e o maior deslocamento.
        /// </summary>
        private static (int moved, float worst) SquareUp(Vector3[] world, float tolerance)
        {
            int moved = 0;
            float worst = 0f;
            for (int a = 0; a < 3; a++)
            {
                float min = world.Min(w => w[a]);
                float max = world.Max(w => w[a]);
                for (int k = 0; k < world.Length; k++)
                {
                    float c = world[k][a];
                    float target = c - min <= tolerance ? min : max - c <= tolerance ? max : c;
                    if (Mathf.Abs(target - c) < 1e-5f)
                        continue;
                    worst = Mathf.Max(worst, Mathf.Abs(target - c));
                    world[k][a] = target;
                    moved++;
                }
            }

            return (moved, worst);
        }

        /// <summary>Reconstrói malha, UV/normais e o collider (o MeshCollider não percebe sozinho).</summary>
        private static void Rebuild(ProBuilderMesh pb)
        {
            if (pb == null)
                return;

            pb.ToMesh();
            pb.Refresh();

            if (pb.TryGetComponent(out MeshCollider mc) && pb.TryGetComponent(out MeshFilter mf))
            {
                mc.sharedMesh = null;
                mc.sharedMesh = mf.sharedMesh;
            }
        }

        private void RebuildScopeMeshes()
        {
            foreach (ProBuilderMesh pb in scope)
                Rebuild(pb);
            lastApplyReport = null;
            if (analyzed)
                Analyze();
            Repaint();
        }

        private static void RebakeNavMesh()
        {
            NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Exclude);
            if (surfaces.Length == 0)
            {
                EditorUtility.DisplayDialog("Alinhar estrutura", "Nenhum NavMeshSurface na cena aberta.", "Ok");
                return;
            }

            NavMeshAssetManager.instance.StartBakingSurfaces(surfaces.Cast<Object>().ToArray());
            Debug.Log($"[AlinharEstrutura] Bake do NavMesh iniciado em {surfaces.Length} surface(s). Salve a cena ao terminar.");
        }

        // --- Scene view -------------------------------------------------------------

        private void DrawSceneOverlay(SceneView view)
        {
            if (!analyzed)
                return;

            foreach (Fix fix in fixes)
            {
                if (!fix.Enabled || fix.Piece.Mesh == null)
                    continue;

                Piece p = fix.Piece;
                Handles.color = fix.Rule switch
                {
                    "Fresta" => new Color(1f, 0.3f, 0.2f),
                    "Emenda" => new Color(1f, 0.8f, 0.1f),
                    "Linha" => new Color(0.3f, 0.8f, 1f),
                    "Esquadro" => new Color(1f, 0.2f, 0.8f),
                    "Espessura" => new Color(0.8f, 0.4f, 1f),
                    _ => new Color(0.6f, 1f, 0.4f)
                };
                var min = new Vector3(p.Min[0], p.Min[1], p.Min[2]);
                var max = new Vector3(p.Max[0], p.Max[1], p.Max[2]);
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
            }
        }

        // --- Utilidades -------------------------------------------------------------

        private GameObject FindRoot()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject r in scene.GetRootGameObjects())
                {
                    if (r.name == rootName)
                        return r;
                }
            }

            return null;
        }

        /// <summary>Pelo nome, SEM espaços nas pontas — há um "Chao " no Complemento.</summary>
        private static bool TryKind(string name, out Kind kind)
        {
            switch (name.Trim())
            {
                case "Parede": kind = Kind.Wall; return true;
                case "Chao": kind = Kind.Floor; return true;
                case "Teto": kind = Kind.Ceiling; return true;
                default: kind = default; return false;
            }
        }

        private static bool IsAxisAligned(Transform t)
        {
            foreach (Vector3 dir in new[] { t.right, t.up, t.forward })
            {
                float m = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
                if (m < 0.999f)
                    return false;
            }

            return true;
        }

        private static void WorldBounds(ProBuilderMesh pb, float[] min, float[] max)
        {
            for (int i = 0; i < 3; i++)
            {
                min[i] = float.PositiveInfinity;
                max[i] = float.NegativeInfinity;
            }

            foreach (Vector3 v in pb.positions)
            {
                Vector3 w = pb.transform.TransformPoint(v);
                for (int i = 0; i < 3; i++)
                {
                    min[i] = Mathf.Min(min[i], w[i]);
                    max[i] = Mathf.Max(max[i], w[i]);
                }
            }
        }

        private static float Overlap(Piece a, Piece b, int axis) =>
            Mathf.Min(a.Max[axis], b.Max[axis]) - Mathf.Max(a.Min[axis], b.Min[axis]);

        private static float Median(IEnumerable<float> values)
        {
            List<float> list = values.OrderBy(v => v).ToList();
            return list.Count == 0 ? 0f : list[list.Count / 2];
        }

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name.Trim();
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return $"{path} #{t.GetSiblingIndex()}";
        }
    }
}
