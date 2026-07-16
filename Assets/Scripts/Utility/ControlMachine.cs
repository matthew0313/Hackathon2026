using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ControlMachine<T>
{
    public readonly Action<T> setter;
    readonly T defaultValue;
    public ControlMachine(Action<T> setter, T defaultValue)
    {
        this.setter = setter;
        this.defaultValue = defaultValue;
        setter.Invoke(defaultValue);
    }
    readonly List<ControlElement<T>> controls = new();
    public void AddControl(ControlElement<T> control)
    {
        if (controls.Contains(control)) return;
        controls.Add(control);
        control.onControlElementUpdate += UpdateControlList;
        UpdateControlList();
    }
    public void RemoveControl(ControlElement<T> control)
    {
        if (!controls.Contains(control)) return;
        controls.Remove(control);
        control.onControlElementUpdate -= UpdateControlList;
        UpdateControlList();
    }
    void UpdateControlList()
    {
        if (controls.Count > 0)
        {
            controls.Sort((a, b) =>
            {
                if (a.enabled == false)
                {
                    if (b.enabled == false) return 0;
                    else return 1;
                }
                if (b.enabled == false) return -1;

                return b.priority.CompareTo(a.priority);
            });
            if (controls[0].enabled)
            {
                setter.Invoke(controls[0].valueGetter.Invoke()); return;
            }
        }
        setter.Invoke(defaultValue);
    }
}
public class ControlElement<T>
{
    public readonly Func<T> valueGetter;
    public readonly int priority;
    bool m_enabled = false;
    public bool enabled
    {
        get => m_enabled;
        set
        {
            m_enabled = value;
            onControlElementUpdate?.Invoke();
        }
    }
    public Action onControlElementUpdate;
    public ControlElement(Func<T> valueGetter, int priority, bool enabled = true)
    {
        this.valueGetter = valueGetter;
        this.priority = priority;
        m_enabled = enabled;
    }
}