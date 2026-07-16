using UnityEngine;
using UnityEngine.Audio;
using System.IO;
using System.Linq;




#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "Sound", menuName = "AudioManagement/Sound")]
public class Sound : ScriptableObject
{
    public AudioResource audioSource;
    public AudioMixerGroup output;
    public bool bypassEffects, bypassListenerEffects, bypassReverbZones;
    public int priority = 128;
    public float volume = 1.0f, pitch = 1.0f, stereoPan = 0.0f, spatialBlend = 0.0f, reverbZoneMix = 1.0f;
    public float dopplerLevel = 1.0f, spread;
    public AudioRolloffMode volumeRolloff;
    public AnimationCurve customRolloffCurve;
    public float minDistance = 1.0f, maxDistance = 500.0f;
    public float startTime = 0.0f;
    public void PasteTo(AudioSource source)
    {
        source.resource = audioSource;
        source.outputAudioMixerGroup = output;
        source.bypassEffects = bypassEffects;
        source.bypassListenerEffects = bypassListenerEffects;
        source.bypassReverbZones = bypassReverbZones;
        source.playOnAwake = false;
        source.priority = priority;
        source.volume = volume;
        source.pitch = pitch;
        source.panStereo = stereoPan;
        source.spatialBlend = spatialBlend;
        source.reverbZoneMix = reverbZoneMix;
        source.dopplerLevel = dopplerLevel;
        source.spread = spread;
        source.rolloffMode = volumeRolloff;
        if(source.rolloffMode == AudioRolloffMode.Custom) source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customRolloffCurve);
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.time = startTime;
    }
}
#if UNITY_EDITOR
[CustomEditor(typeof(Sound))]
public class Sound_Editor : Editor
{
    SerializedProperty audioSource;
    SerializedProperty output;
    SerializedProperty bypassEffects, bypassListenerEffects, bypassReverbZones;
    SerializedProperty priority;
    SerializedProperty volume, pitch, stereoPan, spatialBlend, reverbZoneMix;
    SerializedProperty dopplerLevel, spread;
    SerializedProperty volumeRolloff;
    SerializedProperty customRolloffCurve;
    SerializedProperty minDistance, maxDistance;
    SerializedProperty startTime;

    private AudioSource previewer;

    private void OnEnable()
    {
        audioSource = serializedObject.FindProperty("audioSource");
        output = serializedObject.FindProperty("output");
        bypassEffects = serializedObject.FindProperty("bypassEffects");
        bypassListenerEffects = serializedObject.FindProperty("bypassListenerEffects");
        bypassReverbZones = serializedObject.FindProperty("bypassReverbZones");
        priority = serializedObject.FindProperty("priority");
        volume = serializedObject.FindProperty("volume");
        pitch = serializedObject.FindProperty("pitch");
        stereoPan = serializedObject.FindProperty("stereoPan");
        spatialBlend = serializedObject.FindProperty("spatialBlend");
        reverbZoneMix = serializedObject.FindProperty("reverbZoneMix");
        dopplerLevel = serializedObject.FindProperty("dopplerLevel");
        spread = serializedObject.FindProperty("spread");
        volumeRolloff = serializedObject.FindProperty("volumeRolloff");
        customRolloffCurve = serializedObject.FindProperty("customRolloffCurve");
        minDistance = serializedObject.FindProperty("minDistance");
        maxDistance = serializedObject.FindProperty("maxDistance");
        startTime = serializedObject.FindProperty("startTime");

        previewer = EditorUtility.CreateGameObjectWithHideFlags("Audio preview", HideFlags.HideAndDontSave, typeof(AudioSource)).GetComponent<AudioSource>();
    }

    private void OnDisable()
    {
        if (previewer != null) DestroyImmediate(previewer.gameObject);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(audioSource);
        EditorGUILayout.PropertyField(output);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(bypassEffects, new GUIContent("Bypass Effects"));
        EditorGUILayout.PropertyField(bypassListenerEffects, new GUIContent("Bypass Listener Effects"));
        EditorGUILayout.PropertyField(bypassReverbZones, new GUIContent("Bypass Reverb Zones"));

        EditorGUILayout.Space();

        EditorGUILayout.IntSlider(priority, 0, 256, new GUIContent("Priority"));

        EditorGUILayout.Space();

        EditorGUILayout.Slider(volume, 0, 1, new GUIContent("Volume"));
        EditorGUILayout.Slider(pitch, -3, 3, new GUIContent("Pitch"));

        EditorGUILayout.Slider(stereoPan, -1, 1, new GUIContent("Stereo Pan"));
        EditorGUILayout.Slider(spatialBlend, 0, 1, new GUIContent("Spatial Blend"));
        EditorGUILayout.Slider(reverbZoneMix, 0, 1.1f, new GUIContent("Reverb Zone Mix"));

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("3D Sound Settings", EditorStyles.boldLabel);
        EditorGUILayout.Slider(dopplerLevel, 0, 5, new GUIContent("Doppler Level"));
        EditorGUILayout.Slider(spread, 0, 360, new GUIContent("Spread"));
        EditorGUILayout.PropertyField(volumeRolloff);

        if (volumeRolloff.enumValueIndex == (int)AudioRolloffMode.Custom)
        {
            EditorGUILayout.PropertyField(customRolloffCurve);
        }

        EditorGUILayout.PropertyField(minDistance);
        EditorGUILayout.PropertyField(maxDistance);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Others", EditorStyles.boldLabel);
        startTime.floatValue = EditorGUILayout.FloatField("Start Time", startTime.floatValue);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();

        if (GUILayout.Button("Preview"))
        {
            Sound sound = (Sound)target;
            if (sound.audioSource != null)
            {
                sound.PasteTo(previewer);
                previewer.Play();
            }
        }

        if (GUILayout.Button("Stop"))
        {
            previewer.Stop();
        }
    }

    public override bool HasPreviewGUI() => true;

    public override void OnPreviewGUI(Rect r, GUIStyle background)
    {
        Sound sound = (Sound)target;
        if (sound.audioSource != null)
        {
            AudioClip clip = sound.audioSource as AudioClip;
            Texture2D waveformTexture = AssetPreview.GetAssetPreview(clip);
            if (waveformTexture != null)
            {
                GUI.DrawTexture(r, waveformTexture, ScaleMode.StretchToFill);
            }
        }
    }

    [MenuItem("Assets/Create Sound")]
    public static void CreateSound()
    {
        AudioClip clip = Selection.activeObject as AudioClip;
        Sound asset = CreateInstance<Sound>();
        asset.audioSource = clip;
        string tmp = AssetDatabase.GetAssetPath(clip);
        AssetDatabase.CreateAsset(asset, tmp + ".asset");
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();

        Selection.activeObject = asset;

    }
    [MenuItem("Assets/Create Sound", true)]
    public static bool CreateSoundValidation()
    {
        return Selection.activeObject is AudioClip;
    }
}
#endif