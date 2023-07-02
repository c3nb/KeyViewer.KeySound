using KeyViewer.API;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using static UnityModManagerNet.UnityModManager;
using UnityEngine.Networking;
using SFB;
using System.Diagnostics;

namespace KeyViewer.KeySound
{
    public static class Main
    {
        public const string SettingsPath = "Mods/KeyViewer.KeySound/Settings.json";
        public static ModEntry.ModLogger Logger { get; private set; }
        public static Settings Settings { get; private set; }
        public static void Load(ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            modEntry.OnToggle = (m, v) =>
            {
                InputAPI.EventActive = v;
                if (v)
                {
                    if (File.Exists(SettingsPath))
                        Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath));
                    else Settings = new Settings();
                    InputAPI.OnKeyPressed += OnKeyPressed;
                    EventAPI.OnUpdateKeysLayout += OnUpdateKeys;
                    OnUpdateKeys(KeyViewer.Main.KeyManager);
                    if (!Settings.SetEachKey)
                        ApplyKeys();
                }
                else
                {
                    InputAPI.OnKeyPressed -= OnKeyPressed;
                    EventAPI.OnUpdateKeysLayout -= OnUpdateKeys;
                    File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
                }
                return true;
            };
            modEntry.OnGUI = m =>
            {
                var setEachKey = GUILayout.Toggle(Settings.SetEachKey, "Set Sound Each Keys");
                if (setEachKey != Settings.SetEachKey)
                {
                    Settings.SetEachKey = setEachKey;
                    ApplyKeys();
                    return;
                }
                if (Settings.SetEachKey)
                {
                    foreach (var (code, sound) in Settings.Sounds)
                    {
                        if (sound.guiExpaned = GUILayout.Toggle(sound.guiExpaned, $"{code} Sound Settings"))
                        {
                            MoreGUILayout.BeginIndent();
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Sound File");
                            var newSound = GUILayout.TextField(sound.sound);
                            if (GUILayout.Button("Select File"))
                                newSound = SelectFile("Audio File", "mp3", "ogg", "aiff", "aif", "wav") ?? sound.sound;
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                            if (newSound != sound.sound)
                            {
                                AudioPlayer.StopAll();
                                sound.sound = newSound;
                            }
                            sound.offset = MoreGUILayout.NamedSlider("Offset", sound.offset, 0, 1000, 300);
                            sound.volume = MoreGUILayout.NamedSlider("Volume", sound.volume, 0, 1, 300);
                            sound.pitch = MoreGUILayout.NamedSlider("Pitch", sound.pitch, 0, 10, 300);
                            MoreGUILayout.EndIndent();
                        }
                    }
                }
                else
                {
                    Sound sound = Settings.GlobalSound;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Sound File");
                    var newSound = GUILayout.TextField(sound.sound);
                    if (GUILayout.Button("Select File"))
                        newSound = SelectFile("Audio File", "mp3", "ogg", "aiff", "aif", "wav") ?? sound.sound;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    if (newSound != sound.sound)
                    {
                        AudioPlayer.StopAll();
                        ApplyKeys();
                        sound.sound = newSound;
                    }
                    var newOffset = MoreGUILayout.NamedSlider("Offset", sound.offset, 0, 1000, 300);
                    if (newOffset != sound.offset)
                    {
                        sound.offset = newOffset;
                        ApplyKeys();
                    }
                    var newVolume = MoreGUILayout.NamedSlider("Volume", sound.volume, 0, 1, 300);
                    if (newVolume != sound.volume)
                    {
                        sound.volume = newVolume;
                        ApplyKeys();
                    }
                    var newPitch = MoreGUILayout.NamedSlider("Pitch", sound.pitch, 0, 10, 300);
                    if (newPitch != sound.volume)
                    {
                        sound.pitch = newPitch;
                        ApplyKeys();
                    }
                }
            };
            modEntry.OnSaveGUI += m => File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            //modEntry.OnUpdate = (m, dt) => AudioPlayer.UpdateForMeasure();
        }
        private static void OnUpdateKeys(KeyManager keys)
        {
            Dictionary<KeyCode, Sound> newSounds = new Dictionary<KeyCode, Sound>();
            foreach (var code in keys.Codes)
                if (Settings.Sounds.TryGetValue(code, out var sound))
                    newSounds.Add(code, sound);
                else newSounds.Add(code, new Sound());
            Settings.Sounds = newSounds;
        }
        private static void OnKeyPressed(KeyCode code)
        {
            AudioPlayer.Play(Settings.Sounds[code]);
        }
        public static void ApplyKeys()
        {
            foreach (var code in Settings.Sounds.Keys.ToList())
                Settings.Sounds[code] = Settings.GlobalSound.Copy();
        }
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
        public static string SelectFile(string title, params string[] extensions)
        {
            if (extensions.Length <= 0) return null;
            var extFilters = new ExtensionFilter(null, extensions);
            var files =  StandaloneFileBrowser.OpenFilePanel(title, "", new[] { extFilters }, extensions.Length > 1);
            return files.Length > 0 ? files[0] : null;
        }
        public static string[] SelectFiles(string title, params string[] extensions)
        {
            if (extensions.Length <= 0) return null;
            var extFilters = new ExtensionFilter(null, extensions);
            var files = StandaloneFileBrowser.OpenFilePanel(title, "", new[] { extFilters }, true);
            return files.Length > 0 ? files : null;
        }
    }
    public static class AudioPlayer
    {
        static List<AudioSource> sources = new List<AudioSource>();
        static Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public static void Play(Sound sound)
        {
            if (sound?.sound == null) return;
            if (clips.TryGetValue(sound.sound, out var clip))
            {
                if (sound.offset > 0)
                    StaticCoroutine.Run(PlayCo(sound.SetClip(clip)));
                else
                {
                    AudioSource source = EnsureSource();
                    sound.SetClip(clip);
                    source.clip = sound.clip;
                    source.volume = sound.volume;
                    source.pitch = sound.pitch;
                    source.Play();
                }
            }
            else StaticCoroutine.Run(LoadClip(sound.sound, clip => StaticCoroutine.Run(PlayCo(sound.SetClip(clip)))));
        }
        public static void Stop(Sound sound)
        {
            sources.Find(a => a.clip == sound.clip)?.Stop();
        }
        public static void StopAll()
        {
            sources.ForEach(a => a.Stop());
        }
        static IEnumerator PlayCo(Sound sound)
        {
            float counted = 0f;
            while (counted < sound.offset)
            {
                counted += Time.deltaTime * 1000f;
                yield return null;
            }
            AudioSource source = EnsureSource();
            source.clip = sound.clip;
            source.volume = sound.volume;
            source.pitch = sound.pitch;
            source.Play();
        }
        static IEnumerator LoadClip(string sound, Action<AudioClip> callback)
        {
            if (callback == null) yield break;
            if (clips.TryGetValue(sound, out var c))
            {
                callback(c);
                yield break;
            }
            if (!File.Exists(sound)) yield break;
            Uri.TryCreate(sound, UriKind.RelativeOrAbsolute, out Uri uri);
            if (uri == null) yield break;
            var at = Path.GetExtension(sound) switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".mp3" => AudioType.MPEG,
                ".aiff" => AudioType.AIFF,
                ".wav" => AudioType.WAV,
                _ => AudioType.UNKNOWN
            };
            if (at == AudioType.UNKNOWN) yield break;
            var clipReq = UnityWebRequestMultimedia.GetAudioClip(uri, at);
            yield return clipReq.SendWebRequest();
            var clip = DownloadHandlerAudioClip.GetContent(clipReq);
            UnityEngine.Object.DontDestroyOnLoad(clip);
            callback(clips[sound] = clip);
        }
        static AudioSource EnsureSource()
        {
            var source = sources.FirstOrDefault(a => !a.isPlaying);
            Main.Logger.Log(source != null ? $"Found Idle Source! Returing Back.. ({sources.Count})" : $"Fuck! No Idle Source.. Creating.. ({sources.Count})");
            if (source != null) return source;
            GameObject sourceObject = new GameObject();
            source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.ignoreListenerPause = true;
            source.ignoreListenerVolume = true;
            UnityEngine.Object.DontDestroyOnLoad(sourceObject);
            sources.Add(source);
            return source;
        }
        internal static List<MeasureScope> scopes = new List<MeasureScope>();
        internal static void BeginMeasure(AudioSource source)
        {
            MeasureScope scope = new MeasureScope();
            scope.watch = Stopwatch.StartNew();
            scope.source = source;
            scopes.Add(scope);
        }
        internal static void UpdateForMeasure()
        {
            for (int i = 0; i < scopes.Count; i++)
            {
                var scope = scopes[i];
                if (!scope.source.isPlaying)
                {
                    scope.watch.Stop();
                    Main.Logger.Log($"Audio Play Latency: {scope.watch.ElapsedMilliseconds - scope.source.clip.length * 1000}ms");
                    scopes.RemoveAt(i);
                }
            }
        }
    }
    public class MeasureScope
    {
        public AudioSource source;
        public Stopwatch watch;
    }
    public class Settings
    {
        public bool SetEachKey = false;
        public Sound GlobalSound = new Sound();
        public Dictionary<KeyCode, Sound> Sounds = new Dictionary<KeyCode, Sound>();
    }
    public class Sound
    {
        public Sound() { }
        public string sound;
        public float offset = 0;
        public float volume = 1;
        public float pitch = 1;

        internal bool guiExpaned;
        internal AudioClip clip;
        internal Sound SetClip(AudioClip clip)
        {
            this.clip = clip;
            return this;
        }

        public Sound Copy()
        {
            Sound newSound = new Sound();
            newSound.sound = sound;
            newSound.offset = offset;
            newSound.volume = volume;
            newSound.pitch = pitch;
            return newSound;
        }
    }
}
