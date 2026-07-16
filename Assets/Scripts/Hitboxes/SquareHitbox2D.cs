using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class SquareHitbox2D : Hitbox2D
{
    [SerializeField] LayerMask targetMask;
    [SerializeField] List<string> tagFilter;
    private void OnDrawGizmosSelected()
    {
        var tmp = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.greenYellow;
        Gizmos.DrawWireCube(Vector3.zero, transform.lossyScale);
        Gizmos.matrix = tmp;
    }
    readonly List<RaycastHit2D> list = new();
    public override List<RaycastHit2D> Scan()
    {
        list.Clear();
        foreach (var i in Physics2D.BoxCastAll(transform.position,
            new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)), 0.0f, transform.up, 0.0f, targetMask))
        {
            bool filtered = false;
            foreach(var tag in tagFilter)
            {
                if (i.transform.CompareTag(tag))
                {
                    filtered = true;
                    break;
                }
            }
            if(!filtered) list.Add(i);
        }
        return list;
    }
}