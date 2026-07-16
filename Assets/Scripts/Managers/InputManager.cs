using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single place the game reads input from, so nothing else has to care where it comes from.
/// Loaded from Resources and kept alive across scenes.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Instance = Instantiate(Resources.Load<InputManager>("InputManager"));
        DontDestroyOnLoad(Instance.gameObject);
    }
    public Vector2 MoveInput()
    {
        Vector2 tmp = Vector2.zero;
        if (Input.GetKey(KeyCode.D)) tmp += Vector2.right;
        if (Input.GetKey(KeyCode.A)) tmp += Vector2.left;
        if(Input.GetKey(KeyCode.W)) tmp += Vector2.up;
        if(Input.GetKey(KeyCode.S)) tmp += Vector2.down;
        tmp.Normalize();
        return tmp;
    }
    public Vector2 AimInput(Vector2 playerPos)
    {
        Vector2 mousePos = Input.mousePosition;
        Vector2 worldMousePos = Camera.main.ScreenToWorldPoint(mousePos);
        Vector2 aimDir = (worldMousePos - playerPos).normalized;
        return aimDir;
    }
}