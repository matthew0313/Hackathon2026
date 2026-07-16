using System.Collections.Generic;
using UnityEngine;

public abstract class Hitbox2D : MonoBehaviour
{
    public abstract List<RaycastHit2D> Scan();
}
