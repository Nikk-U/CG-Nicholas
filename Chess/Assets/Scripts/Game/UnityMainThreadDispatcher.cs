using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

/// <summary>
/// A thread-safe class which holds a queue with actions to execute on the next Update() method. It can be used to make calls to the main thread for
/// things such as UI Manipulation in Unity. It was developed for use in combination with the Firebase Unity plugin, which uses separate threads for event handling
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;

    public static UnityMainThreadDispatcher Instance() {
        if (!Dispatched) {
            var go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    public static bool Dispatched { get { return _instance != null; } }

    void Awake() {
        if (_instance == null) {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    void Update() {
        lock(_executionQueue) {
            while (_executionQueue.Count > 0) {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// Locks the queue and adds the Action to the queue
    /// </summary>
    /// <param name="action">Function that will be executed from the main thread.</param>
    public void Enqueue(Action action) {
        lock (_executionQueue) {
            _executionQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// Enqueue an action with a delay
    /// </summary>
    /// <param name="action">Function that will be executed from the main thread.</param>
    /// <param name="delay">The delay in seconds.</param>
    /// <returns>A task that represents the delay operation.</returns>
    public async Task EnqueueWithDelay(Action action, float delay) {
        await Task.Delay(TimeSpan.FromSeconds(delay));
        Enqueue(action);
    }

    /// <summary>
    /// Enqueues an IEnumerator to be executed on the main thread and waits for its completion
    /// </summary>
    /// <param name="coroutine">The coroutine to execute.</param>
    public async Task EnqueueCoroutine(IEnumerator coroutine) {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        
        Enqueue(() => {
            _instance.StartCoroutine(RunCoroutineWithCompletion(coroutine, tcs));
        });
        
        await tcs.Task;
    }
    
    private IEnumerator RunCoroutineWithCompletion(IEnumerator coroutine, TaskCompletionSource<bool> tcs) {
        yield return StartCoroutine(coroutine);
        tcs.SetResult(true);
    }
}