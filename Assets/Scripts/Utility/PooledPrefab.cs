using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class PooledPrefab<T> : MonoBehaviour where T : PooledPrefab<T>
{
    static Dictionary<GameObject, List<T>> pools = null;
    GameObject prefabOrigin;
    bool instantiated = false;
    public bool released { get; private set; } = false;
    protected virtual T Create()
    {
        T tmp = Instantiate(this as T);
        tmp.instantiated = true;
        tmp.prefabOrigin = gameObject;
        tmp.OnCreate();
        return tmp;
    }
    protected virtual void OnCreate() { }
    public T Get()
    {
        if (instantiated) return null;

        T tmp;
        if (pools == null) pools = new();
        if (!pools.ContainsKey(gameObject)) pools.Add(gameObject, new());
        if (pools[gameObject].Count == 0) tmp = Create();
        else
        {
            tmp = pools[gameObject][0];
            pools[gameObject].RemoveAt(0);
            tmp.released = false;
        }
        tmp.OnTakeout();
        return tmp;
    }
    public T Get(Vector3 position, Quaternion rotation)
    {
        T tmp = Get();
        if(tmp != null)
        {
            tmp.transform.position = position;
            tmp.transform.rotation = rotation;
        }
        return tmp;
    }
    public T Get(Transform parent)
    {
        T tmp = Get();
        if (tmp != null) tmp.transform.SetParent(parent, false);
        return tmp;
    }
    public T Get(Vector3 localPos, Quaternion localRot, Transform parent)
    {
        T tmp = Get(parent);
        if (tmp != null)
        {
            tmp.transform.localPosition = localPos;
            tmp.transform.localRotation = localRot;
        }
        return tmp;
    }
    public virtual void Release()
    {
        if (released) return;

        OnRelease();
        pools[prefabOrigin].Add(this as T);
        released = true;
    }
    protected virtual void OnTakeout() => gameObject.SetActive(true);
    protected virtual void OnRelease() => gameObject.SetActive(false);
    protected virtual void OnDestroy()
    {
        if (released) pools[prefabOrigin].Remove(this as T);
    }
}
public class PooledPrefab : PooledPrefab<PooledPrefab>
{

}