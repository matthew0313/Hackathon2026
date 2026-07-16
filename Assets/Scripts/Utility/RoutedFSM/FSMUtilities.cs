using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class WaitThen<T> : State<T>
{
    readonly Func<float> waitTimeGetter;
    readonly Action onFinish;
    public WaitThen(T origin, Layer<T> parent, Func<float> waitTimeGetter, Action onFinish) : base(origin, parent)
    {
        this.onFinish = onFinish;
        this.waitTimeGetter = waitTimeGetter;
    }
    float counter = 0.0f;
    bool finished = false;
    public override void OnStateEnter()
    {
        base.OnStateEnter();
        counter = 0.0f;
        finished = false;
    }
    public override void OnStateUpdate()
    {
        base.OnStateUpdate();
        if (finished) return;

        counter += Time.deltaTime;
        if(counter >= waitTimeGetter.Invoke())
        {
            finished = true;
            onFinish?.Invoke();
        }
    }
}