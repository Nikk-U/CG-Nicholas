using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manager component that initializes and manages the GameStatePersistence system.
/// Attach this to a GameObject in your scene to enable game state persistence.
/// </summary>
public class GameStatePersistenceManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the SimpleUIController if you want to update UI")]
    [SerializeField] private SimpleUIController uiController;

    [Header("Debug Settings")]
    [SerializeField] private bool logDebugMessages = true;

    private void Start()
    {
        // Initialize the persistence system
        InitializeGameStatePersistence();
        
        // Subscribe to chess network status updates to show state persistence messages
        ChessNetworkManager.OnNetworkStatusChanged += OnChessNetworkStatusChanged;
    }

    private void OnDestroy()
    {
        // Unsubscribe from network status events
        ChessNetworkManager.OnNetworkStatusChanged -= OnChessNetworkStatusChanged;
    }

    /// <summary>
    /// Create and initialize the GameStatePersistence object
    /// </summary>
    private void InitializeGameStatePersistence()
    {
        // Check if GameStatePersistence already exists
        if (GameStatePersistence.Instance == null)
        {
            GameObject persistenceObj = new GameObject("GameStatePersistence");
            GameStatePersistence persistence = persistenceObj.AddComponent<GameStatePersistence>();
            
            // Add NetworkObject component
            NetworkObject netObj = persistenceObj.AddComponent<NetworkObject>();
            
            if (logDebugMessages)
            {
                Debug.Log("PERSISTENCE MANAGER: Created GameStatePersistence object");
            }
            
            // Spawn the persistence object on the network if we're the server
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !netObj.IsSpawned)
            {
                netObj.Spawn();
                
                if (logDebugMessages)
                {
                    Debug.Log("PERSISTENCE MANAGER: Spawned GameStatePersistence on network");
                }
            }
        }
        else if (logDebugMessages)
        {
            Debug.Log("PERSISTENCE MANAGER: GameStatePersistence already exists");
        }
    }

    /// <summary>
    /// Handle network status changes from ChessNetworkManager
    /// </summary>
    /// <param name="status">The status message</param>
    private void OnChessNetworkStatusChanged(string status)
    {
        // Listen for reconnection events
        if (status.Contains("reconnect") || status.Contains("Reconnect"))
        {
            if (GameStatePersistence.Instance != null)
            {
                if (logDebugMessages)
                {
                    Debug.Log("PERSISTENCE MANAGER: Detected reconnection, requesting state restoration");
                }
                
                // Request state restoration
                GameStatePersistence.Instance.RequestStateRestoration();
            }
        }
    }

    /// <summary>
    /// Called by SimpleUIController when the Rejoin button is clicked
    /// </summary>
    public void OnRejoinRequested()
    {
        if (NetworkManager.Singleton != null && 
            NetworkManager.Singleton.IsConnectedClient &&
            GameStatePersistence.Instance != null)
        {
            if (logDebugMessages)
            {
                Debug.Log("PERSISTENCE MANAGER: Manual rejoin requested, triggering state restoration");
            }
            
            // Request state restoration
            GameStatePersistence.Instance.RequestStateRestoration();
        }
    }
}