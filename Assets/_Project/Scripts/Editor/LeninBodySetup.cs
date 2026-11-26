using System.IO;
using System.Linq;
using TheDelivery.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ATO 5, FASE 2 (2026-11-26): o sequestrador ganha o corpo do Lenin (o estranho do Interlúdio, Leaning.fbx).
    /// O Leaning da Rua continua intocado: aqui se usa uma CÓPIA (Models/Lenin.fbx) importada como Humanoid, para
    /// tocar as mesmas animações humanoides do killer (AntagonistFSM: Idle/Walking/Running/Sneak).
    /// Troca o filho "StandardIdle" do Kidnapper por "LeninBody" (mesma altura) e renomeia o objeto para "Lenin".
    /// Marcador Temp/LeninBodySetup.pending; relatório em Temp/LeninBodySetup.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class LeninBodySetup
    {
        private const string ScenePath = "Assets/Scenes/Escape.unity";
        private const string Src = "Assets/_Project/Models/Leaning.fbx";
        private const string Dst = "Assets/_Project/Models/Lenin.fbx";
        private const string Controller = "Assets/_Project/Animation/Controllers/AntagonistFSM.controller";
        private const float RoadScale = 146.35f;
        private const float HeadHeight = 1.65f;
        private static double next;

        static LeninBodySetup() => EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/LeninBodySetup.pending")) return;
            File.Delete("Temp/LeninBodySetup.pending"); Run();
        };

        [MenuItem("Tools/The Delivery/Ato 5 - Corpo do Lenin")]
        public static void Run()
        {
            var log = new System.Text.StringBuilder();
            // 1) a cópia humanoide
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Dst) == null)
            {
                AssetDatabase.CopyAsset(Src, Dst);
                log.AppendLine("Lenin.fbx copiado do Leaning.fbx.");
            }
            var imp = (ModelImporter)AssetImporter.GetAtPath(Dst);
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = false;
                imp.SaveAndReimport();
                log.AppendLine("Lenin.fbx importado como Humanoid.");
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Dst).OfType<Avatar>().FirstOrDefault();
            log.AppendLine($"Avatar: {(avatar != null ? avatar.name : "NENHUM")} válido={avatar != null && avatar.isValid} humano={avatar != null && avatar.isHuman}");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dst);
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var ai = Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a.gameObject.scene == scene);
                if (ai == null) { log.AppendLine("SEM AntagonistAI!"); return; }
                var root = ai.transform;
                Transform old = root.Find("StandardIdle") ?? root.Find("LeninBody");
                float targetH = 1.8f; Vector3 lp = Vector3.zero; Quaternion lr = Quaternion.identity;
                if (old != null)
                {
                    var b = Bounds(old.gameObject); if (b.size.y > 0.5f) targetH = b.size.y;
                    lp = old.localPosition; lr = old.localRotation;
                    Object.DestroyImmediate(old.gameObject);
                }
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                body.name = "LeninBody";
                body.transform.SetParent(root, false);
                body.transform.localPosition = lp; body.transform.localRotation = lr; body.transform.localScale = Vector3.one;
                float h = Bounds(body).size.y;
                // a MESMA escala do Leaning na Rua (Road.unity): o Lenin tem o mesmo tamanho nos dois atos
                body.transform.localScale = Vector3.one * (RoadScale / Mathf.Max(0.0001f, root.lossyScale.y));
                // pés no chão (pivô do agente)
                var bb = Bounds(body);
                body.transform.position += Vector3.up * (root.position.y - bb.min.y);
                var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl; anim.avatar = avatar; anim.applyRootMotion = false;
                // altura pelo esqueleto (os limites do SkinnedMesh no editor vêm inflados): cabeça a 1,65 m do chão
                anim.Rebind();
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                var foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (head != null && foot != null)
                {
                    float hh = head.position.y - root.position.y;
                    if (hh > 0.01f) body.transform.localScale *= HeadHeight / hh;
                    log.AppendLine($"Cabeça estava a {hh:0.00} m -> {HeadHeight} m.");
                }
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.gameObject.layer;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                root.gameObject.name = "Lenin";
                log.AppendLine($"Corpo trocado: altura final {Bounds(body).size.y:0.00} m (antes: corpo do killer {targetH:0.00} m), escala {body.transform.localScale.x:0.000}.");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                File.WriteAllText("Temp/LeninBodySetup.txt", log.ToString());
                Debug.Log("[LeninBodySetup]\n" + log);
            }
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
