using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
{
    public List<MyKeyValuePair<TKey, TValue>> content = new();
    public void OnAfterDeserialize()
    {
        for (int i = 0; i < content.Count; i++) Add(content[i].key, content[i].value);
    }

    public void OnBeforeSerialize()
    {
        foreach (var i in this)
        {
            content.Add(new MyKeyValuePair<TKey, TValue> { key = i.Key, value = i.Value });
        }
    }
}
[System.Serializable]
public struct MyKeyValuePair<TKey, TValue>
{
    public TKey key;
    public TValue value;
}