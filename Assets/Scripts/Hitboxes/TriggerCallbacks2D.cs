using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class TriggerCallbacks2D : MonoBehaviour
{
    public event Action<Collider2D> onTriggerEnter2D, onTriggerStay2D, onTriggerExit2D;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        onTriggerEnter2D?.Invoke(collision);
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        onTriggerStay2D?.Invoke(collision);
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        onTriggerExit2D?.Invoke(collision);
    }
}