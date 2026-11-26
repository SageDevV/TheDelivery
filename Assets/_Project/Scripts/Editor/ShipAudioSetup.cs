using System.Linq;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONS DO ATO 5 (fase 4, 2026-11-26; ElevenLabs, flow "The Delivery - Ato 5 (navio)"). Liga os 18 sons às peças do
    /// navio: ambiente em loop, gemidos do casco (avulsos), gotas no metal/água (DripEmitter), passos na água (WaterZone),
    /// partida e motor dos geradores, portão subindo, bipe do painel, cela, gancho, chapa, balde, sinais do Lenin (porta,
    /// passos e cantarolar ao longe), passos do Lenin (CreatureFootsteps) e passos da Clear no aço. Chamado no fim do
    /// <see cref="ShipFinishSetup"/>.
    /// </summary>
    public static class ShipAudioSetup
    {
        private static AudioClip C(string n)
        {
            foreach (var e in new[] { ".wav", ".mp3", ".ogg" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/" + n + e);
                if (c != null) return c;
            }
            return null;
        }
        private static AudioClip[] Many(string prefix, int n) =>
            Enumerable.Range(1, n).Select(i => C($"{prefix}_{i:00}")).Where(c => c != null).ToArray();

        private static void Set(Object o, string prop, Object v)
        {
            var so = new SerializedObject(o); var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[ShipAudio] {o.GetType().Name}.{prop} não existe"); return; }
            p.objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArr(Object o, string prop, AudioClip[] v)
        {
            var so = new SerializedObject(o); var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[ShipAudio] {o.GetType().Name}.{prop} não existe"); return; }
            p.arraySize = v.Length; for (int i = 0; i < v.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = v[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Apply(Transform R, Scene scene, System.Text.StringBuilder log)
        {
            int n = 0;
            // ambiente do porão (2D, loop) + gemidos avulsos do casco
            var amb = new GameObject("ShipAudio"); amb.transform.SetParent(R, false);
            var src = amb.AddComponent<AudioSource>();
            src.clip = C("ShipAmbienceLoop"); src.loop = true; src.playOnAwake = true; src.volume = 0.85f; src.spatialBlend = 0f;
            var groans = amb.AddComponent<AmbientOneShots>();
            SetArr(groans, "clips", Many("ShipHullGroan", 4)); n += 2;

            // gotas
            var metal = Many("ShipDripMetal", 4); var water = Many("ShipDripWater", 4);
            foreach (var de in R.GetComponentsInChildren<DripEmitter>(true))
            {
                var so = new SerializedObject(de); bool wet = so.FindProperty("wet").boolValue;
                de.SetImpactClips(wet ? water : metal); EditorUtility.SetDirty(de);
                SetArr(de, "impactClips", wet ? water : metal);
            }
            n += 2;

            // água: passos de quem anda dentro dela
            foreach (var wz in R.GetComponentsInChildren<WaterZone>(true)) SetArr(wz, "wadeSteps", Many("ShipWadeStep", 6));
            n++;

            // geradores
            foreach (var g in R.GetComponentsInChildren<GeneratorStation>(true))
            { Set(g, "startClip", C("ShipGeneratorStart")); Set(g, "runLoop", C("ShipGeneratorLoop")); }
            n += 2;

            // portão e painel
            foreach (var gate in R.GetComponentsInChildren<ShipGate>(true))
            { Set(gate, "openClip", C("ShipGateOpen")); Set(gate, "powerClip", C("ShipPanelBeep")); }
            n += 2;

            // puzzle da cela
            foreach (var pz in R.GetComponentsInChildren<PuzzleInteractable>(true))
            {
                string clip = pz.Id switch
                {
                    "CellLock" => "ShipCellUnlock", "KeyReach" => "ShipHookKey", "LoosePanel" => "ShipPanelPry", "Bucket" => "ShipBucket", _ => null
                };
                if (clip != null) Set(pz, "useClip", C(clip));
            }
            var door = R.GetComponentsInChildren<Door>(true).FirstOrDefault(d => d.name == "CellDoor");
            if (door != null) Set(door, "openClip", null);       // o rangido já está no som da fechadura
            n += 4;

            // diretor: sinais do Lenin + passos da Clear no aço
            var dir = Object.FindObjectsByType<Act5Director>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.gameObject.scene == scene);
            if (dir != null)
            {
                SetArr(dir, "signalClips", new[] { C("ShipDistantSlam"), C("ShipDistantSteps"), C("ShipLeninHumming") }.Where(c => c != null).ToArray());
                SetArr(dir, "shipFootsteps", Many("ShipClearStep", 8));
                n += 4;
            }

            // passos do Lenin no aço
            var lenin = Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a.gameObject.scene == scene);
            if (lenin != null)
            {
                var fs = lenin.GetComponent<CreatureFootsteps>(); if (fs == null) fs = lenin.gameObject.AddComponent<CreatureFootsteps>();
                var so = new SerializedObject(fs);
                so.FindProperty("enableFootsteps").boolValue = true;
                so.FindProperty("footstepMode").intValue = (int)FootstepMode.PerStepClips;
                var arr = so.FindProperty("footstepClips"); var steps = Many("ShipLeninStep", 8);
                arr.arraySize = steps.Length; for (int i = 0; i < steps.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = steps[i];
                so.FindProperty("stepCadence").intValue = (int)FootstepCadence.Distance;
                so.FindProperty("stepStride").floatValue = 0.8f;
                so.FindProperty("stepPitchRange").vector2Value = new Vector2(0.93f, 1.04f);
                so.FindProperty("stepVolume").floatValue = 0.95f;
                so.FindProperty("basePitch").floatValue = 1f;
                so.FindProperty("matchPitchToPace").boolValue = false;
                so.FindProperty("spatialBlend").floatValue = 1f;
                so.FindProperty("minDistance").floatValue = 1.5f;
                so.FindProperty("maxDistance").floatValue = 24f;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            log.AppendLine($"Sons do Ato 5 ligados ({n} ligações).");
        }
    }
}
