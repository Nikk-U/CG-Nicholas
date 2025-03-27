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
            BroadcastStatus("NETWORK INIT: Chess Network Manager initialized and ready");
        }
        else
        {
            Debug.Log("INIT MESSAGE: NetworkManager.Singleton is null during initialization");
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
            Debug.Log("HOST SETUP: Host player initialized with White pieces");
            BroadcastStatus("GAME CREATED: You are hosting the game as White");
        }
    }
    
    /// <summary>
    /// Called when a client connects to the network
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"CONNECTION ALERT: Client {clientId} has connected to the game");
        
        // If the connecting player is not the host, assign them to the black side
        if (clientId != NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost)
        {
            // Add client to players dictionary as Black
            playerSides[clientId] = Side.Black;
            lastConnectedClientId = clientId;
            
            // Inform the host that a client joined
            BroadcastStatus($"PLAYER JOINED: Client {clientId} connected and assigned to Black side");
            
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
        Debug.Log($"DISCONNECT NOTICE: Client {clientId} has disconnected from the game");
        
        // Don't remove the client from playerSides to preserve their side when they rejoin
        if (NetworkManager.Singleton.IsHost)
        {
            BroadcastStatus($"OPPONENT LEFT: Client {clientId} disconnected. Their position as Black is reserved for reconnection");
        }
        else if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            BroadcastStatus("CONNECTION LOST: You were disconnected from the host server");
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
            Debug.Log($"ROLE ASSIGNMENT: Local player assigned to {assignedSide} side");
            
            // Add this client to the local players dictionary
            playerSides[clientId] = assignedSide;
            
            // Update the UI
            BroadcastStatus($"SIDE CONFIRMED: You will be playing as {assignedSide}. Waiting for game to begin");
        }
    }
    
    /// <summary>
    /// Starts the game on all clients
    /// </summary>
    [ClientRpc]
    private void StartGameClientRpc()
    {
        isMultiplayerGameActive = true;
        Debug.Log("GAME LAUNCH: Multiplayer chess match is now starting");
        
        // Tell the GameManager to start a new game if it exists
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartNewGame();
            Debug.Log("BOARD SETUP: New chess game initialized via GameManager");
        }
        
        // Make sure ChessMoveRelay is active
        EnsureMoveRelayIsActive();
        
        // Update pieces to only allow movement of own side
        UpdatePieceControl();
        
        BroadcastStatus("MATCH STARTED: Game is now in progress. White moves first");
    }
    
    private void EnsureMoveRelayIsActive()
    {
        // Find or create the ChessMoveRelay
        ChessRelay relay = FindObjectOfType<ChessRelay>();
        if (relay == null)
        {
            Debug.Log("RELAY CREATION: Setting up new ChessMoveRelay for network synchronization");
            GameObject relayObj = new GameObject("ChessMoveRelay");
            relay = relayObj.AddComponent<ChessRelay>();
            
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
                Debug.Log("RELAY ACTIVATED: ChessMoveRelay successfully spawned on network");
            }
        }
        else
        {
            Debug.Log("RELAY FOUND: Using existing ChessMoveRelay for network communication");
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
        {
            // STRICT ROLE ENFORCEMENT - No exceptions
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                // Host can NEVER move black pieces under any circumstances
                if (NetworkManager.Singleton.IsHost && side == Side.Black)
                {
                    Debug.Log("SECURITY NOTE: Host attempted to control Black pieces");
                    return false;
                }
        
                // Client can NEVER move white pieces under any circumstances
                if (!NetworkManager.Singleton.IsHost && side == Side.White)
                {
                    Debug.Log("SECURITY NOTE: Client attempted to control White pieces");
                    return false;
                }
            }
        }
        
        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            // In single player mode, allow all moves
            return true;
        }
        
        // If we're the host, we can only control White
        if (NetworkManager.Singleton.IsHost)
        {
            return side == Side.White;
        }
        
        // If we're a client, we can only control Black
        return side == Side.Black;
    }
    
    /// <summary>
    /// Updates piece controls to only allow movement of the player's side
    /// </summary>
    public void UpdatePieceControl()
    {
        if (!NetworkManager.Singleton.IsConnectedClient || !isMultiplayerGameActive)
            return;
    
        // Get all visual pieces
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>(true);

        foreach (VisualPiece piece in allPieces)
        {
            if (NetworkManager.Singleton.IsHost)
            {
                // Host can only move white pieces, disable black pieces
                bool isPlayerPiece = piece.PieceColor == Side.White;
                piece.enabled = isPlayerPiece;
        
                // Make sure interaction components are also disabled
                if (piece.GetComponent<Collider>() != null)
                    piece.GetComponent<Collider>().enabled = isPlayerPiece;
            }
            else
            {
                // Client can only move black pieces, disable white pieces
                bool isPlayerPiece = piece.PieceColor == Side.Black;
                piece.enabled = isPlayerPiece;
        
                // Make sure interaction components are also disabled
                if (piece.GetComponent<Collider>() != null)
                    piece.GetComponent<Collider>().enabled = isPlayerPiece;
            }
        }

        if (NetworkManager.Singleton.IsHost)
            BroadcastStatus("CONTROL ENABLED: You are the host playing as White");
        else
            BroadcastStatus("CONTROL ENABLED: You are the client playing as Black");
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
        Debug.Log($"NETWORK STATUS: {message}");
        OnNetworkStatusChanged?.Invoke(message);
    }
}