using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class KeepAccrossScenes : MonoBehaviour
{
    [SerializeField]
    private string persistenceKey;

    private static readonly Dictionary<string, KeepAccrossScenes>
        instances = new Dictionary<string, KeepAccrossScenes>();

    private string registeredKey;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        instances.Clear();
    }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(persistenceKey))
        {
            Debug.LogError(
                $"[Persistence] Assign a key on {gameObject.name}.", this);
            return;
        }

        if (instances.TryGetValue(persistenceKey, out var existing) &&
            existing != null && existing != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        registeredKey = persistenceKey;
        instances[registeredKey] = this;

        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (registeredKey != null &&
            instances.TryGetValue(registeredKey, out var existing) &&
            existing == this)
        {
            instances.Remove(registeredKey);
        }
    }
}