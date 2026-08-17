using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Liga a trilha de ambiente da Cafeteria (Ato 1): põe um GameObject com
    /// <see cref="AmbientMusic"/> na cena, aponta para o <c>cafeteria_ambient</c> e ajusta o
    /// import do clipe.
    ///
    /// O AJUSTE DE IMPORT É O MOTIVO DESTE MENU EXISTIR — o resto é arrastar um componente. O
    /// <c>cafeteria_ambient.mp3</c> tem 8,5 MB e vinha como <b>Decompress On Load</b>, que
    /// descomprime a música INTEIRA para PCM na memória ao carregar a cena (dezenas de MB de
    /// RAM, e um engasgo no load). Para um leito de ambiente que toca do começo ao fim o certo é
    /// <b>Streaming</b>: lê do disco conforme toca, com pegada de memória constante.
    ///
    /// Idempotente: pode rodar de novo: o GameObject existente é reaproveitado, não duplicado.
    /// </summary>
    public static class AmbientMusicSetup
    {
        private const string ScenePath = "Assets/Scenes/Cafeteria.unity";
        private const string ClipPath = "Assets/_Project/SoundEffects/cafeteria_ambient.mp3";
        private const string HostName = "AmbientMusic";

        [MenuItem("Tools/The Delivery/Setup Ambient Music (Cafeteria)")]
        private static void Run()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null)
            {
                Fail($"Clipe não encontrado em:\n{ClipPath}");
                return;
            }

            ConfigureStreaming(ClipPath);

            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            AmbientMusic ambient = FindAmbient(scene);
            if (ambient == null)
            {
                var host = new GameObject(HostName);
                Undo.RegisterCreatedObjectUndo(host, "Add Ambient Music");
                SceneManager.MoveGameObjectToScene(host, scene);
                ambient = Undo.AddComponent<AmbientMusic>(host);
            }

            AssignClip(ambient, clip);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorGUIUtility.PingObject(ambient.gameObject);
            Debug.Log(
                $"[AmbientMusicSetup] Trilha \"{clip.name}\" ligada na Cafeteria ({clip.length:0} s, " +
                "Streaming). Ajuste o volume no componente AmbientMusic — ambiente costuma ficar " +
                "entre 0.15 e 0.35 para não competir com o diálogo.",
                ambient.gameObject);
        }

        /// <summary>
        /// Põe o clipe em Streaming e liga o load em background. Ver a doc da classe para o
        /// porquê. No-op se já estiver assim — evita um reimport de 8 MB a cada execução.
        /// </summary>
        private static void ConfigureStreaming(string clipPath)
        {
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (importer == null)
                return;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming && importer.loadInBackground)
                return;

            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }

        /// <summary>Acha o AmbientMusic da cena, inclusive inativo.</summary>
        private static AmbientMusic FindAmbient(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<AmbientMusic>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void AssignClip(AmbientMusic ambient, AudioClip clip)
        {
            var so = new SerializedObject(ambient);
            SerializedProperty property = so.FindProperty("clip");
            if (property == null)
                return;

            property.objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ambient);
        }

        /// <summary>
        /// Garante a Cafeteria aberta, oferecendo salvar o que estiver em outra cena antes.
        /// Mesmo contrato do <c>MarinaSetup</c>.
        /// </summary>
        private static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[AmbientMusicSetup] {message}");
            EditorUtility.DisplayDialog("Setup Ambient Music", message, "OK");
        }
    }
}
