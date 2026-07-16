using System;
using System.Collections.Generic;

public static class MathUtilities
{
    public static ulong GenerateRandomID()
    {
        int upper = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        int lower = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        return (ulong)((upper << 32) + lower);
    }
}