using System.Net.Http;
using UnityEngine;
using UnityEngine.InputSystem;

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
    Pooler<AudioSource> audioSourcePool;
    private void Awake()
    {
        audioSourcePool = new(() => new GameObject().AddComponent<AudioSource>());
    }
    public AudioSource PlaySound(Sound sound)
    {
        var source = audioSourcePool.GetObject();
        sound.PasteTo(source);
        source.Play();
        return source;
    }
}