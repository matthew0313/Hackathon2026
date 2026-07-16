using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single place the game reads input from, so nothing else has to care where it comes from.
/// Loaded from Resources and kept alive across scenes.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Instance = Instantiate(Resources.Load<AudioManager>("AudioManager"));
        DontDestroyOnLoad(Instance.gameObject);
    }
    readonly List<AudioSource> sources = new();
    Pooler<AudioSource> audioSourcePool;
    private void Awake()
    {
        audioSourcePool = new(() => new GameObject().AddComponent<AudioSource>());
    }
    private void Update()
    {
        for(int i = 0; i < sources.Count; i++)
        {
            if (!sources[i].isPlaying)
            {
                audioSourcePool.ReleaseObject(sources[i]);
                sources.RemoveAt(i--);
            }
        }
    }
    public AudioSource PlaySound(Sound sound)
    {
        var source = audioSourcePool.GetObject();
        sound.PasteTo(source);
        source.Play();
        sources.Add(source);
        return source;
    }
}