using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class HeartUI : MonoBehaviour
{
    [SerializeField] Image image;
    [SerializeField] Sprite activeImage, inactiveImage;
    public void Set(bool active)
    {
        image.sprite = active ? activeImage : inactiveImage;
    }
}