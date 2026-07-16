using MEC;
using NavMeshPlus.Components;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    [SerializeField] Image image;
    [SerializeField] float transitionTime = 3.0f;
    [SerializeField] float damageEffectTime = 0.5f;

    [SerializeField] Transform heartAnchor;
    [SerializeField] HeartUI heartPrefab;
    [SerializeField] NavMeshSurface surface;
    [SerializeField] List<EnemySpawner> spawners;
    [SerializeField] Exit exit;
    readonly List<HeartUI> hearts = new();
    Player player;
    private void Awake()
    {
        Instance = this;
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
        player.onDamage += OnDamage;
        player.onHpChange += OnHpChange;
        surface.BuildNavMesh();
        for(int i = 0; i < player.maxHp; i++)
        {
            hearts.Add(Instantiate(heartPrefab, heartAnchor));
            hearts[i].gameObject.SetActive(true);
        }
    }
    private void Update()
    {
        for(int i = 0; i < spawners.Count; i++)
        {
            if (spawners[i].dead) spawners.RemoveAt(i--);
        }
        if (spawners.Count == 0) exit.gameObject.SetActive(true);
    }

    private void OnHpChange()
    {
        for(int i = 0; i < hearts.Count; i++)
        {
            hearts[i].Set(player.hp > i);
        }
    }

    CoroutineHandle currentScreenEffect;
    private void OnDamage(int damage)
    {
        currentScreenEffect = Timing.RunCoroutine(DamageEffect());
    }
    IEnumerator<float> DamageEffect()
    {
        float counter = 0.0f;
        while (counter <= damageEffectTime)
        {
            counter += Time.deltaTime;
            image.color = Color.Lerp(new Color(1, 0, 0, 0.5f), new Color(1, 0, 0, 0.0f), counter / damageEffectTime);
            yield return Timing.WaitForOneFrame;
        }
    }
    bool ended = false;

    public void GameWin()
    {
        if (ended) return; ended = true;
        Timing.KillCoroutines(currentScreenEffect);
        Timing.RunCoroutine(Transition(Color.white, "Clear"));
    }
    public void GameOver()
    {
        if (ended) return; ended = true;
        Timing.KillCoroutines(currentScreenEffect);
        Timing.RunCoroutine(Transition(new Color(0.8f, 0.0f, 0.0f), "Game_Over"));
    }
    IEnumerator<float> Transition(Color color, string sceneName)
    {
        float counter = 0.0f;
        while(counter <= transitionTime)
        {
            counter += Time.deltaTime;
            Color color2 = color; color2.a = 0;
            image.color = Color.Lerp(color2, color, counter / transitionTime);
            yield return Timing.WaitForOneFrame;
        }
        SceneManager.LoadScene(sceneName);
    }
}