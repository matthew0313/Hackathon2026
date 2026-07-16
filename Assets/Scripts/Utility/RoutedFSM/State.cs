using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class State<T>
{
    protected readonly T origin;
    public readonly Layer<T> parentLayer;
    protected virtual TopLayer<T> root => parentLayer.root;
    protected virtual FSMVals values => parentLayer.values;
    public State(T origin, Layer<T> parent)
    {
        this.origin = origin;
        this.parentLayer = parent;
    }
    public virtual void OnStateEnter()
    {
        root.AlertStateChange();
    }
    public virtual void RefreshState() { }
    public virtual void OnStateUpdate() { }
    public virtual void OnStateFixedUpdate() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateDrawGizmos() { }
    public virtual void OnStateDrawGizmosSelected() { }
    public virtual string GetFSMPath()
    {
        return parentLayer.GetStateName(this);
    }
}
