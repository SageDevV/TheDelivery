using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>Diagnóstico (2026-11-25): estado do áudio do Editor. Temp/AudioStateProbe.pending -> .txt</summary>
    [InitializeOnLoad]
    public static class AudioStateProbe
    {
        static AudioStateProbe() => EditorApplication.update += () =>
        {
            if (!File.Exists("Temp/AudioStateProbe.pending")) return;
            File.Delete("Temp/AudioStateProbe.pending");
            var c = AudioSettings.GetConfiguration();
            File.WriteAllText("Temp/AudioStateProbe.txt",
                $"audioMasterMute={EditorUtility.audioMasterMute} listenerPause={AudioListener.pause} listenerVol={AudioListener.volume} " +
                $"sampleRate={c.sampleRate} speaker={c.speakerMode} dsp={c.dspBufferSize} real={c.numRealVoices} driver={AudioSettings.driverCapabilities}");
        };
    }
}
