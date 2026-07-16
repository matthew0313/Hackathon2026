using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DataUnit
{
    public SerializableDictionary<string, int> integers = new();
    public SerializableDictionary<string, ulong> ulongs = new();
    public SerializableDictionary<string, float> floats = new();
    public SerializableDictionary<string, string> strings = new();
    public SerializableDictionary<string, bool> bools = new();
    public void SaveVector2(string key, Vector2 value)
    {
        floats[key + ".x"] = value.x;
        floats[key + ".y"] = value.y;
    }
    public bool TryLoadVector2(string key, out Vector2 value)
    {
        value = new();
        if (floats.TryGetValue(key + ".x", out float x) && floats.TryGetValue(key + ".y", out float y))
        {
            value = new Vector2(x, y);
            return true;
        }
        return false;
    }
    public Vector2 LoadVector2(string key)
    {
        return new Vector2(floats[key + ".x"], floats[key + ".y"]);
    }
    public void SaveColor(string key, Color value)
    {
        floats[key + ".r"] = value.r;
        floats[key + ".g"] = value.g;
        floats[key + ".b"] = value.b;
        floats[key + ".a"] = value.a;
    }
    public bool TryLoadColor(string key, out Color color)
    {
        color = new();
        if(floats.TryGetValue(key + ".r", out float r)
            && floats.TryGetValue(key + ".g", out float g)
            && floats.TryGetValue(key + ".b", out float b)
            && floats.TryGetValue(key + ".a", out float a))
        {
            color = new Color(r, g, b, a);
            return true;
        }
        return false;
    }
    public Color LoadColor(string key)
    {
        return new Color(floats[key + ".r"], floats[key + ".g"], floats[key + ".b"], floats[key + ".a"]);
    }
}