using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class Layer<T> : State<T>
{
    protected Dictionary<string, State<T>> states = new();
    protected Dictionary<State<T>, string> stateNames = new();
    protected State<T> currentState = null;
    protected State<T> defaultState = null;
    public Layer(T origin, Layer<T> parent) : base(origin, parent)
    {
        
    }
    protected void AddState(string name, State<T> state)
    {
        states[name] = state;
        stateNames[state] = name;
    }
    public string GetStateName(State<T> state)
    {
        if (!stateNames.ContainsKey(state))
        {
            Debug.LogError("State not found. Make sure you're using AddState() to add states.");
            return null;
        }
        else return stateNames[state];
    }
    public void ChangeState(params string[] stateRoute) => ChangeState(stateRoute.ToList());
    void ChangeState(List<string> stateRoute)
    {
        if (stateRoute.Count <= 0) return;
        if (!states.ContainsKey(stateRoute[0]))
        {
            Debug.LogError("Tried to access state that does not exist");
            return;
        }
        currentState.OnStateExit();
        currentState = states[stateRoute[0]];
        if(currentState is Layer<T> layer)
        {
            if (stateRoute.Count > 1)
            {
                stateRoute.RemoveAt(0);
                layer.ChangeState(stateRoute);
            }
            else layer.currentState = layer.defaultState;
        }
        currentState.OnStateEnter();
    }
    public override void OnStateEnter()
    {
        base.OnStateEnter();
        if (currentState == null) currentState = defaultState;
        currentState.OnStateEnter();
    }
    public override void OnStateExit()
    {
        base.OnStateExit();
        currentState.OnStateExit();
    }
    public override void OnStateUpdate()
    {
        base.OnStateUpdate();
        currentState.OnStateUpdate();
    }
    public override void OnStateFixedUpdate()
    {
        base.OnStateFixedUpdate();
        currentState.OnStateFixedUpdate();
    }
    public override string GetFSMPath()
    {
        return $"{base.GetFSMPath()}->{currentState.GetFSMPath()}";
    }
}
