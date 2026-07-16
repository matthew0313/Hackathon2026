using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Exit : MonoBehaviour
{
    Player player;
    private void OnEnable()
    {
        player ??= GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        GameObject hitObject = collision.attachedRigidbody != null ? collision.attachedRigidbody.gameObject : collision.gameObject;
        if (hitObject == player.gameObject)
        {
            player.rb.simulated = false;
            GameManager.Instance.GameWin();
        }
    }
}