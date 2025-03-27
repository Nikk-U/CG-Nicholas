using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityChess;

/// <summary>
/// Manages the network functionality for the chess game.
/// </summary>
public class ChessNetworkManager : NetworkBehaviour
{
    // Singleton instance
    public static ChessNetworkManager Instance { get; private set; }
    
    // Network status event that UI can subscribe to
    public delegate void NetworkStatusEvent(string status);
    public static event NetworkStatusEvent OnNetworkStatusChanged;
    
    // Dictionary to store connected players and their assigned sides
    private Dictionary<ulong, Side> playerSides = new Dictionary<ulong, Side>();
    
    // Game state
    private bool isMultiplayerGameActive = false;
    
    // Track the last connected client ID
    private ulong lastConnectedClientId = 0;
    
    private void Awake()
    {
        // Ensure singleton behavior
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
        // Set up network event handlers
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            BroadcastStatus("Chess Network Manager ready");
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton is null in ChessNetworkManager Start");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }
    }
    
    /// <summary>
    /// Initialize this instance as a host
    /// </summary>
    public void InitializeAsHost()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            playerSides[NetworkManager.Singleton.LocalClientId] = Side.White;
            Debug.Log("Host initialized as White");
            BroadcastStatus("Game hosted. You are playing as White.");
        }
    }
    
    /// <summary>
    /// Called when a client connects to the network
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        
        // If the connecting player is not the host, assign them to the black side
        if (clientId != NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost)
        {
            // Add client to players dictionary as Black
            playerSides[clientId] = Side.Black;
            lastConnectedClientId = clientId;
            
            // Inform the host that a client joined
            BroadcastStatus($"Client {clientId} connected and assigned to Black");
            
            // Tell the client they are the black side
            AssignPlayerSideClientRpc(clientId, (int)Side.Black);
            
            // Start the game when both players are connected
            StartGameClientRpc();
        }
    }
    
    /// <summary>
    /// Called when a client disconnects from the network
    /// </summary>
    private void OnClientDisconnect(ulong clientId)
    {
        Debug.Log($"Client disconnected: {clientId}");
        
        // Don't remove the client from playerSides to preserve their side when they rejoin
        if (NetworkManager.Singleton.IsHost)
        {
            BroadcastStatus($"Client {clientId} disconnected. Their side (Black) is reserved for reconnection.");
        }
        else if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            BroadcastStatus("You were disconnected from the server.");
        }
    }
    
    /// <summary>
    /// Assigns a side to a client
    /// </summary>
    [ClientRpc]
    private void AssignPlayerSideClientRpc(ulong clientId, int sideValue)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            Side assignedSide = (Side)sideValue;
            Debug.Log($"I've been assigned side: {assignedSide}");
            
            // Add this client to the local players dictionary
            playerSides[clientId] = assignedSide;
            
            // Update the UI
            BroadcastStatus($"You are playing as {assignedSide}. Waiting for game to start.");
        }
    }
    
    /// <summary>
    /// Starts the game on all clients
    /// </summary>
    [ClientRpc]
    private void StartGameClientRpc()
    {
        isMultiplayerGameActive = true;
        Debug.Log("Game starting!");
        
        // Tell the GameManager to start a new game if it exists
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartNewGame();
            Debug.Log("New game started via GameManager");
        }
        
        // Make sure ChessMoveRelay is active
        EnsureMoveRelayIsActive();
        
        // Update pieces to only allow movement of own side
        UpdatePieceControlClientRpc();
        
        BroadcastStatus("Game started! White to move first.");
    }
    
    /// <summary>
    /// Client-side RPC to update piece controls
    /// </summary>
    [ClientRpc]
    private void UpdatePieceControlClientRpc()
    {
        // Get the local client's side
        Side localSide = GetPlayerSide(NetworkManager.Singleton.LocalClientId);
        
        // Get all visual pieces
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>(true);
        
        foreach (VisualPiece piece in allPieces)
        {
            // Only enable pieces for the local player's side
            piece.enabled = piece.PieceColor == localSide;
        }
        
        // Broadcast local status
        BroadcastStatus($"Updating piece controls. Your side: {localSide}");
    }
    
    private void EnsureMoveRelayIsActive()
    {
        // Find or create the ChessMoveRelay
        ChessMoveRelay relay = FindObjectOfType<ChessMoveRelay>();
        if (relay == null)
        {
            Debug.Log("Creating new ChessMoveRelay GameObject");
            GameObject relayObj = new GameObject("ChessMoveRelay");
            relay = relayObj.AddComponent<ChessMoveRelay>();
            
            // Assign the board reference
            GameObject board = GameObject.FindGameObjectWithTag("Board");
            if (board != null)
            {
                relay.chessBoard = board;
            }
            
            // Add NetworkObject component
            NetworkObject netObj = relayObj.AddComponent<NetworkObject>();
            
            // Spawn the relay object if we're the server
            if (NetworkManager.Singleton.IsServer && !netObj.IsSpawned)
            {
                netObj.Spawn();
                Debug.Log("ChessMoveRelay spawned successfully");
            }
        }
        else
        {
            Debug.Log("ChessMoveRelay already exists");
        }
    }
    
    /// <summary>
    /// Returns the side (White/Black) assigned to the specified client
    /// </summary>
    public Side GetPlayerSide(ulong clientId)
    {
        if (playerSides.TryGetValue(clientId, out Side side))
        {
            return side;
        }
        
        // If this is a client reconnecting and was previously assigned Black
        if (NetworkManager.Singleton.IsHost && clientId != NetworkManager.Singleton.LocalClientId)
        {
            // Auto-assign Black to any non-host client that connects
            return Side.Black;
        }
        
        // If this is the host reconnecting, they are White
        if (clientId == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost)
        {
            return Side.White;
        }
        
        return Side.None;
    }
    
    /// <summary>
    /// Checks if the local player is allowed to move pieces of the given side
    /// </summary>
    public bool CanControlSide(Side side)
    {
        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            // In single player mode, allow all moves
            return true;
        }
        
        // Get the local client's side
        Side localSide = GetPlayerSide(NetworkManager.Singleton.LocalClientId);
        
        // Only allow control of the local player's side
        return side == localSide;
    }
    
    /// <summary>
    /// Updates piece controls to only allow movement of the player's side
    /// </summary>
    public void UpdatePieceControl()
    {
        if (!NetworkManager.Singleton.IsConnectedClient || !isMultiplayerGameActive)
            return;
        
        // Call the client RPC to update piece controls on all clients
        UpdatePieceControlClientRpc();
    }
    
    /// <summary>
    /// Check if we're in a multiplayer game
    /// </summary>
    public bool IsInMultiplayerGame()
    {
        return isMultiplayerGameActive && NetworkManager.Singleton.IsConnectedClient;
    }
    
    /// <summary>
    /// Broadcast a status message to all listeners
    /// </summary>
    public void BroadcastStatus(string message)
    {
        Debug.Log($"Chess Network: {message}");
        OnNetworkStatusChanged?.Invoke(message);
    }
}