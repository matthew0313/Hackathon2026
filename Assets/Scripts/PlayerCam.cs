using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCam : MonoBehaviour
{
    [field: SerializeField] public CinemachineCamera cineCam { get; private set; }
    [SerializeField] float lerpRate = 10.0f;
    Player player;
    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
    }
    private void LateUpdate()
    {
        transform.position = Vector2.Lerp(transform.position, player.transform.position, lerpRate * Time.deltaTime);
        transform.position += new Vector3(0, 0, -10.0f);
    }
}