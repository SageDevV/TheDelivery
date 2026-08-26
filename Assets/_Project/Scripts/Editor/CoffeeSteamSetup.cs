using System.Collections.Generic;
using System.IO;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Monta a FUMAÇA DO CAFÉ nas xícaras da cena. Cria (uma vez só, e reaproveita depois):
    /// a textura de fiapo de vapor, o material URP de partícula, e — dentro de cada xícara —
    /// um filho "FumacaCafe" com o <see cref="CoffeeSteam"/> já medido pela malha da xícara.
    ///
    /// Uso: selecione a(s) xícara(s) na Hierarchy e rode
    /// <c>Tools ▸ The Delivery ▸ FX - Fumaça do Café (Xícaras Selecionadas)</c>. Se você só
    /// quer ligar tudo de uma vez, use a variante <c>(Todas as Xícaras da Cena)</c>.
    ///
    /// IDEMPOTENTE: rodar de novo numa xícara que já fumega remede e reconfigura o filho
    /// existente — não empilha um segundo emissor. Mas é "reconfigurar do ZERO": o comando
    /// devolve o preset padrão e descarta o tunning manual que estiver no Inspector. Faça o
    /// ajuste fino DEPOIS de rodar, no Inspector do "FumacaCafe" (comece por
    /// <c>Intensity</c>). A textura e o material também são regerados a cada execução.
    /// </summary>
    public static class CoffeeSteamSetup
    {
        /// <summary>Nome do filho criado dentro da xícara. Também é a chave da idempotência.</summary>
        private const string SteamChildName = "FumacaCafe";

        private const string TextureFolder = "Assets/_Project/Textures/FX";
        private const string MaterialFolder = "Assets/_Project/Materials/FX";
        private const string TexturePath = TextureFolder + "/CoffeeSteamPuff.png";
        private const string MaterialPath = MaterialFolder + "/CoffeeSteam.mat";

        /// <summary>
        /// Pedaços de nome que denunciam uma xícara. De propósito NÃO inclui "cafe": a cena
        /// está cheia de "Mesa_Cafeteria", "BancadaCafeteria", "Lampada_Cafeteria" — todas
        /// iam ganhar fumaça saindo do meio.
        /// </summary>
        private static readonly string[] CupNameHints = { "xicara", "xícara", "caneca", "mug", "teacup" };

        [MenuItem("Tools/The Delivery/FX - Fumaça do Café (Xícaras Selecionadas)")]
        public static void SetupSelectedCups()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length == 0)
            {
                Debug.LogWarning("[CoffeeSteam] Selecione a(s) xícara(s) na Hierarchy antes de rodar o comando " +
                                 "(ou use 'FX - Fumaça do Café (Todas as Xícaras da Cena)').");
                return;
            }

            Setup(selection);
        }

        [MenuItem("Tools/The Delivery/FX - Fumaça do Café (Todas as Xícaras da Cena)")]
        public static void SetupAllCupsInScene()
        {
            var cups = new List<GameObject>();

            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (IsCupName(candidate.name))
                        cups.Add(candidate.gameObject);
                }
            }

            if (cups.Count == 0)
            {
                Debug.LogWarning("[CoffeeSteam] Nenhum objeto com nome de xícara (xicara/caneca/mug) na cena aberta. " +
                                 "Selecione a xícara na Hierarchy e use o comando 'Xícaras Selecionadas'.");
                return;
            }

            Setup(cups.ToArray());
        }

        private static void Setup(IReadOnlyList<GameObject> cups)
        {
            Material material = GetOrCreateMaterial();
            if (material == null)
                return;

            int created = 0;
            int updated = 0;
            int skipped = 0;

            foreach (GameObject cup in cups)
            {
                // Só objetos DA CENA: num prefab no Project não dá pra medir a pose real.
                if (cup == null || EditorUtility.IsPersistent(cup))
                    continue;

                if (!TryGetCupBounds(cup, out Bounds bounds))
                {
                    Debug.LogWarning($"[CoffeeSteam] '{cup.name}' não tem malha visível — sem bounds pra medir a boca " +
                                     "da xícara. Selecione o objeto que tem o MeshRenderer.", cup);
                    skipped++;
                    continue;
                }

                Transform existing = cup.transform.Find(SteamChildName);
                GameObject steamObject;

                if (existing != null)
                {
                    steamObject = existing.gameObject;
                    Undo.RegisterFullObjectHierarchyUndo(steamObject, "Configurar Fumaça do Café");
                    updated++;
                }
                else
                {
                    steamObject = new GameObject(SteamChildName);
                    Undo.RegisterCreatedObjectUndo(steamObject, "Criar Fumaça do Café");
                    Undo.SetTransformParent(steamObject.transform, cup.transform, "Criar Fumaça do Café");
                    created++;
                }

                CoffeeSteam steam = steamObject.GetComponent<CoffeeSteam>();
                if (steam == null)
                    steam = Undo.AddComponent<CoffeeSteam>(steamObject); // traz o ParticleSystem junto

                var renderer = steamObject.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;

                // Volta ao preset ANTES de medir: campos serializados sobrevivem a mudanças
                // no default do código, então sem isto uma xícara montada numa versão antiga
                // ficaria presa nos números antigos pra sempre. O preço é que este comando
                // descarta o tunning manual — é "reconfigurar do zero", não "atualizar".
                steam.ResetToPreset();
                steam.FitTo(bounds);
                EditorUtility.SetDirty(steam);
            }

            if (created + updated > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[CoffeeSteam] {created} xícara(s) fumegando pela primeira vez, {updated} reconfigurada(s), " +
                      $"{skipped} sem malha. Ajuste fino no Inspector do filho '{SteamChildName}' — " +
                      "comece pelo Intensity; o gizmo branco mostra o disco de onde o vapor sai.");
        }

        private static bool IsCupName(string name)
        {
            string lower = name.ToLowerInvariant();

            foreach (string hint in CupNameHints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Soma os bounds (em mundo) de todas as malhas da xícara — corpo, alça e o que mais
        /// o FBX trouxer. Ignora o próprio emissor de fumaça: incluir as partículas já no ar
        /// faria a caixa crescer a cada vez que o comando rodasse.
        /// </summary>
        private static bool TryGetCupBounds(GameObject cup, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer renderer in cup.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;

                if (any)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    any = true;
                }
            }

            return any;
        }

        /// <summary>
        /// Devolve o material da fumaça, criando-o na primeira vez e RECONFIGURANDO-O sempre
        /// (a textura é regerada junto). O asset é reaproveitado — a GUID não muda, então
        /// xícaras já montadas continuam apontando pro mesmo material. Ele é COMPARTILHADO
        /// por todas: mexeu nele, mudou em todas.
        /// </summary>
        private static Material GetOrCreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[CoffeeSteam] Shader 'Universal Render Pipeline/Particles/Unlit' não encontrado. " +
                               "O projeto está em URP? Sem ele não dá pra criar o material da fumaça.");
                return null;
            }

            Texture2D texture = CreateTexture();

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool isNew = material == null;

            if (isNew)
                material = new Material(shader) { name = "CoffeeSteam" };
            else
                material.shader = shader;

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);

            // Transparência com blend ALPHA. Estes valores são o que o Inspector do URP
            // escreveria ao escolher Surface Type = Transparent / Blending Mode = Alpha;
            // como não dá pra chamar a GUI do shader por código, escrevemos na mão.
            material.SetOverrideTag("RenderType", "Transparent"); // o URP LÊ esta tag pra
                                                                 // decidir soft particles
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;

            // SOFT PARTICLES: sem isso, o quad da partícula corta a borda da xícara com uma
            // linha reta — o defeito que mais denuncia fumaça falsa. O PC_RPAsset já tem
            // Depth Texture ligado, que é o requisito.
            // Fade largo (15 cm): quanto mais longa a transição contra a geometria, menos a
            // partícula denuncia que é um quad plano atravessando a borda da xícara.
            const float nearFade = 0f;
            const float farFade = 0.15f;
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetFloat("_SoftParticlesNearFadeDistance", nearFade);
            material.SetFloat("_SoftParticlesFarFadeDistance", farFade);
            material.SetVector("_SoftParticleFadeParams", new Vector4(nearFade, 1f / (farFade - nearFade), 0f, 0f));
            material.EnableKeyword("_SOFTPARTICLES_ON");

            // Fade por distância de câmera desligado (o vapor é pequeno e sempre perto).
            material.SetFloat("_CameraFadingEnabled", 0f);
            material.SetVector("_CameraFadeParams", new Vector4(0f, Mathf.Infinity, 0f, 0f));

            if (isNew)
            {
                EnsureFolder(MaterialFolder);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();

            return material;
        }

        /// <summary>
        /// (Re)gera o fiapo de vapor por código. É SEMPRE regerado, nunca reaproveitado: é um
        /// asset derivado desta função, então melhorias aqui precisam chegar em quem já rodou
        /// o comando antes. Se você pintar uma textura à mão, aponte o material pra ela e pare
        /// de rodar este comando.
        ///
        /// A silhueta é o ponto crítico. Um disco com queda radial — por mais suave que seja —
        /// ainda tem contorno CIRCULAR, e vinte círculos empilhados leem como bolhas de sabão,
        /// não como fumaça. Então aqui o ruído não só modula o brilho: ele ERODE a silhueta,
        /// abrindo buracos e recortes irregulares. Depois vai borrão por cima, porque borda
        /// nítida de qualquer formato é o que o olho usa pra contar as partículas.
        /// </summary>
        private static Texture2D CreateTexture()
        {
            const int size = 256;
            var alpha = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;

                    // 0 no centro, 1 na borda do quad.
                    float distance = Mathf.Clamp01(Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) / 0.5f);

                    // Expoente alto = massa concentrada no meio e beirada morrendo cedo,
                    // muito antes de encostar no limite do quad.
                    float shape = Mathf.Pow(1f - distance, 2.4f);

                    float noise = Fbm(u * 3f, v * 3f);

                    // Erosão: onde o ruído é baixo, o fiapo simplesmente não existe. O Lerp
                    // com piso em 0.1 impede que o miolo fique rendado demais e vire algodão.
                    float erode = Mathf.SmoothStep(0.32f, 0.88f, noise);

                    alpha[y * size + x] = shape * Mathf.Lerp(0.1f, 1f, erode);
                }
            }

            Blur(alpha, size, 3);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float distance = Mathf.Clamp01(Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) / 0.5f);

                    // O blur espalha alpha pra fora; sem esta máscara final o fiapo encosta na
                    // borda do quad e a partícula ganha de volta um contorno reto e visível.
                    float edgeMask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - distance) * 2.2f));

                    float value = Mathf.Clamp01(alpha[y * size + x] * edgeMask);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(value * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            EnsureFolder(TextureFolder);
            File.WriteAllBytes(Path.GetFullPath(TexturePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true; // sem isso, a borda do fiapo puxa preto
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        /// <summary>
        /// Box blur separável, aplicado <paramref name="passes"/> vezes. Três passadas de box
        /// aproximam um gaussiano — barato e suficiente pra matar o granulado de alta
        /// frequência que o Perlin deixa e que, na tela, vira textura de "bolha".
        /// </summary>
        private static void Blur(float[] values, int size, int passes)
        {
            var temp = new float[values.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                const int radius = 2;

                // Horizontal.
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float sum = 0f;
                        for (int k = -radius; k <= radius; k++)
                            sum += values[y * size + Mathf.Clamp(x + k, 0, size - 1)];
                        temp[y * size + x] = sum / (radius * 2 + 1);
                    }
                }

                // Vertical.
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float sum = 0f;
                        for (int k = -radius; k <= radius; k++)
                            sum += temp[Mathf.Clamp(y + k, 0, size - 1) * size + x];
                        values[y * size + x] = sum / (radius * 2 + 1);
                    }
                }
            }
        }

        /// <summary>Ruído somado em cinco oitavas, normalizado em 0..1.</summary>
        private static float Fbm(float x, float y)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float frequency = 1f;
            float total = 0f;

            for (int octave = 0; octave < 5; octave++)
            {
                sum += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return sum / total;
        }

        /// <summary>Cria a pasta (e as pastas-pai que faltarem) se ela ainda não existir.</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
