using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using System;

public class Pooler<T> where T : Component
{
    readonly List<T> pool = new();
    readonly int maxSize;
    readonly Func<T> creator;

    public Action<T> onCreate, onTakeout, onRelease;
    public Pooler(T prefab, int maxSize = 100, int defaultSize = 0)
    {
        creator = () => MonoBehaviour.Instantiate(prefab);
        this.maxSize = maxSize;
        for (int i = 0; i < defaultSize; i++) 
        {
            Release(creator.Invoke());
        }
    }
    public Pooler(Func<T> creator, int maxSize = 100, int defaultSize = 0)
    {
        this.creator = creator;
        this.maxSize = maxSize;
        for (int i = 0; i < defaultSize; i++)
        {
            Release(creator.Invoke());
        }
    }
    public T GetObject()
    {
        T obj = Get();
        OnTakeout(obj);
        return obj;
    }
    public T GetObject(Vector3 position, Quaternion rotation)
    {
        T obj = Get();
        obj.transform.position = position;
        obj.transform.rotation = rotation;
        OnTakeout(obj);
        return obj;
    }
    public T GetObject(Vector3 position, Quaternion rotation, Transform parent)
    {
        T obj = Get();
        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsLastSibling();
        OnTakeout(obj);
        return obj;
    }
    public T GetObject(Transform parent)
    {
        T obj = Get();
        obj.transform.SetParent(parent, false);
        obj.transform.SetAsLastSibling();
        OnTakeout(obj);
        return obj;
    }
    void OnTakeout(T obj)
    {
        if (onTakeout == null) obj.gameObject.SetActive(true);
        else onTakeout.Invoke(obj);
    }
    public void ReleaseObject(T obj)
    {
        Release(obj);
    }
    T Get()
    {
        pool.RemoveAll((T obj) => obj == null || !obj.gameObject.scene.IsValid());
        if(pool.Count == 0)
        {
            var tmp = creator.Invoke();
            onCreate?.Invoke(tmp);
            Release(tmp);
        }
        T result = pool[0];
        pool.RemoveAt(0);
        return result;
    }
    void Release(T obj)
    {
        if (obj == null || !obj.gameObject.scene.IsValid()) return;
        if(pool.Count >= maxSize)
        {
            MonoBehaviour.Destroy(pool[0].gameObject);
            pool.RemoveAt(0);
        }
        OnRelease(obj);
        pool.Add(obj);
    }
    void OnRelease(T obj)
    {
        if(onRelease == null) obj.gameObject.SetActive(false);
        else onRelease.Invoke(obj);
    }
}