using System;
using Unity.Netcode;
using UnityEngine;
using UnityChess;

/// <summary>
/// Maintains game state persistence when clients disconnect and reconnect.
/// Works alongside ChessNetworkManager to ensure game continuity.
/// </summary>
public class GameStatePersistence : NetworkBehaviour
{
    // Singleton instance
    public static GameStatePersistence Instance { get; private set; }
    
    // The serialized game state that will be synchronized
    [SerializeField] private NetworkVariable<GameStateData> gameStateData = new NetworkVariable<GameStateData>(
        new GameStateData { serializedGameState = "", lastMoveIndex = 0 },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Track if we need to restore state on client reconnection
    private bool needsStateRestoration = false;
    
    // Flag to track if we're currently in the restoration process
    private bool isRestoringState = false;
    
    // Flag to track if we're a host that is reconnecting
    private bool isReconnectingHost = false;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }

        // Subscribe to game events
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Check if there's a SimpleUIController in the scene to coordinate with
        SimpleUIController uiController = FindObjectOfType<SimpleUIController>();
        if (uiController != null)
        {
            Debug.Log("STATE PERSISTENCE: Found SimpleUIController, will coordinate state management");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        // Unsubscribe from game events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
    }

    /// <summary>
    /// Called when a move is executed to update the persisted game state
    /// </summary>
    private void OnMoveExecuted()
    {
        // Skip if we're currently in the process of restoring state
        if (isRestoringState)
            return;
            
        if (IsServer || IsHost)
        {
            Debug.Log("STATE PERSISTENCE: Updating persisted game state after move execution");
            UpdatePersistedGameState();
        }
    }

    /// <summary>
    /// Update the networked game state data
    /// </summary>
    private void UpdatePersistedGameState()
    {
        if (GameManager.Instance != null)
        {
            string serializedGame = GameManager.Instance.SerializeGame();
            int moveIndex = GameManager.Instance.LatestHalfMoveIndex;

            GameStateData newState = new GameStateData
            {
                serializedGameState = serializedGame,
                lastMoveIndex = moveIndex
            };

            gameStateData.Value = newState;
            
            Debug.Log($"STATE PERSISTENCE: Game state updated - Move index: {moveIndex}");
        }
    }

    /// <summary>
    /// Called when a client connects to the server
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // If this is a host reconnecting, set the flag to prevent state restoration
        if (SimpleUIController.WasHostBeforeDisconnect && clientId == NetworkManager.Singleton.LocalClientId)
        {
            isReconnectingHost = true;
            Debug.Log("STATE PERSISTENCE: Detected reconnecting host - will preserve current state");
            return;
        }
        
        // If we're not on the server/host, and we're the connecting client
        if (!IsServer && !IsHost && clientId == NetworkManager.Singleton.LocalClientId)
        {
            needsStateRestoration = true;
            Debug.Log("STATE PERSISTENCE: Client connected, flagging for state restoration");
        }
    }

    /// <summary>
    /// Called when a client disconnects from the server
    /// </summary>
    private void OnClientDisconnect(ulong clientId)
    {
        // For server/host, ensure the game state is up-to-date when someone disconnects
        if ((IsServer || IsHost) && clientId != NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log($"STATE PERSISTENCE: Client {clientId} disconnected, updating final state");
            UpdatePersistedGameState();
        }
    }

    private void Update()
    {
        // Handle state restoration for reconnecting clients
        if (needsStateRestoration && IsClient && NetworkManager.Singleton.IsConnectedClient)
        {
            // Do not restore state if we're the host
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || isReconnectingHost)
            {
                Debug.Log("STATE PERSISTENCE: Skipping state restoration because we are host or server");
                needsStateRestoration = false;
                isReconnectingHost = false;
                return;
            }
            
            RestoreGameState();
            needsStateRestoration = false;
        }
    }

    /// <summary>
    /// Restores the game state on a client that has reconnected
    /// </summary>
    private void RestoreGameState()
    {
        // Skip restoration entirely for hosts
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || isReconnectingHost)
        {
            Debug.Log("STATE PERSISTENCE: CRITICAL SAFETY CHECK - Skipping restoration for host");
            return;
        }
        
        if (gameStateData.Value.serializedGameState.Length > 0)
        {
            Debug.Log("STATE PERSISTENCE: Restoring game state from network data");
            
            try
            {
                // Set flag that we're in the restoration process
                isRestoringState = true;
                
                // Load the game from the serialized state
                GameManager.Instance.LoadGame(gameStateData.Value.serializedGameState);
                
                // Ensure the board visual state is updated
                BoardManager.Instance.OnGameResetToHalfMove();
                
                // Update network-specific piece control
                if (ChessNetworkManager.Instance != null)
                {
                    ChessNetworkManager.Instance.UpdatePieceControl();
                }
                
                Debug.Log($"STATE PERSISTENCE: Successfully restored game to move index {gameStateData.Value.lastMoveIndex}");
                
                // Reset restoration flag
                isRestoringState = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"STATE PERSISTENCE: Error restoring game state: {e.Message}");
                isRestoringState = false;
            }
        }
        else
        {
            Debug.Log("STATE PERSISTENCE: No game state to restore");
        }
    }

    /// <summary>
    /// Manually request state restoration (can be called from UI if needed)
    /// </summary>
    public void RequestStateRestoration()
    {
        // Skip restoration for hosts
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || isReconnectingHost || SimpleUIController.WasHostBeforeDisconnect)
        {
            Debug.Log("STATE PERSISTENCE: Ignoring manual state restoration request for host");
            return;
        }
        
        needsStateRestoration = true;
        Debug.Log("STATE PERSISTENCE: Manual state restoration requested");
    }
}

/// <summary>
/// Struct to hold the serialized game state data that will be synchronized over the network
/// </summary>
public struct GameStateData : INetworkSerializable, IEquatable<GameStateData>
{
    public string serializedGameState;
    public int lastMoveIndex;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref serializedGameState);
        serializer.SerializeValue(ref lastMoveIndex);
    }

    public bool Equals(GameStateData other)
    {
        return serializedGameState == other.serializedGameState && 
               lastMoveIndex == other.lastMoveIndex;
    }
}