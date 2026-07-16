using System;
using System.Collections.Generic;
using UnityEngine;

public class PooledEffect : PooledPrefab<PooledEffect>
{
    [SerializeField] float duration = 3.0f;
    float counter = 0.0f;
    protected override void OnTakeout()
    {
        base.OnTakeout();
        counter = 0.0f;
    }
    private void Update()
    {
        counter += Time.deltaTime;
        if (counter > duration) Release();
    }
}