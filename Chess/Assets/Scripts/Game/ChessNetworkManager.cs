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
    public bool PlayerIsLeavingVoluntarily { get; set; } = false;
    // Network status event that UI can subscribe to
    public delegate void NetworkStatusEvent(string status);
    public static event NetworkStatusEvent OnNetworkStatusChanged;
    
    // Dictionary to store connected players and their assigned sides
    private Dictionary<ulong, Side> playerSides = new Dictionary<ulong, Side>();
    
    // Game state
    private bool isMultiplayerGameActive = false;
    
    // Track the last connected client ID
    private ulong lastConnectedClientId = 0;
    
    // Flag to skip auto-start on reconnection
    private bool skipAutoStartGame = false;
    
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
    
        bool isVoluntaryLeave = PlayerIsLeavingVoluntarily;
    
        // Reset the flag immediately after checking it
        PlayerIsLeavingVoluntarily = false;
    
        // Don't remove the client from playerSides to preserve their side when they rejoin
        if (NetworkManager.Singleton.IsHost)
        {
            if (isVoluntaryLeave)
            {
                // This was a voluntary leave - don't treat as resignation
                BroadcastStatus($"OPPONENT LEFT: Client {clientId} left the game voluntarily.");
            }
            else 
            {
                BroadcastStatus($"OPPONENT LEFT: Client {clientId} disconnected. Their position as Black is reserved for reconnection");
            
                // Only treat as resignation if not a voluntary leave
                // If we're in an active game and the client (not host) disconnects, treat as resignation
                if (isMultiplayerGameActive && clientId != NetworkManager.Singleton.LocalClientId)
                {
                    // Get the disconnected player's side (should be Black if client)
                    Side disconnectedSide = GetPlayerSide(clientId);
                    Side winningSide = disconnectedSide == Side.White ? Side.Black : Side.White;
                
                    // Notify all clients of the disconnection win
                    NotifyGameEndClientRpc((int)GameEndReason.Disconnection, (int)winningSide);
                }
            }
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
    
   [ClientRpc]
   private void StartGameClientRpc()
   {
       // Clear any win/lose messages at the start of a new game
       if (UIManager.Instance != null && UIManager.Instance.resultText != null)
       {
           UIManager.Instance.resultText.gameObject.SetActive(false);
       }
    
       // Improve reconnection detection for host
       if (NetworkManager.Singleton.IsHost && skipAutoStartGame)
       {
           Debug.Log("GAME LAUNCH: Host detected during reconnection - preserving current game state");
           skipAutoStartGame = false;
        
           // Make sure pieces have correct control settings
           UpdatePieceControl();
        
           // Ensure the move relay is active
           EnsureMoveRelayIsActive();
        
           BroadcastStatus("RECONNECTION: Client has reconnected. Game continues from current state.");
           return;
       }
    
    
    if (!NetworkManager.Singleton.IsHost && skipAutoStartGame)
    {
        Debug.Log("GAME LAUNCH: Client detected during reconnection - waiting for state");
        skipAutoStartGame = false;
        
        // Client just updates piece control and waits for state
        UpdatePieceControl();
        
        // Ensure the move relay is active
        EnsureMoveRelayIsActive();
        
        BroadcastStatus("RECONNECTION: Successfully reconnected to host. Waiting for game state...");
        return;
    }

    // If we reach here, this is a normal game start, not a reconnection
    isMultiplayerGameActive = true;
    Debug.Log("GAME LAUNCH: New multiplayer chess match is now starting");
    
    // CRITICAL FIX: Only tell GameManager to start a new game if THIS IS NOT a host reconnection
    if (GameManager.Instance != null && 
        !(NetworkManager.Singleton.IsHost && SimpleUIController.WasHostBeforeDisconnect))
    {
        GameManager.Instance.StartNewGame();
        Debug.Log("BOARD SETUP: New chess game initialized via GameManager");
    }
    else if (NetworkManager.Singleton.IsHost && SimpleUIController.WasHostBeforeDisconnect)
    {
        Debug.Log("BOARD PRESERVATION: Host reconnection detected - SKIPPING new game creation to preserve board state");
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
    
    /// <summary>
    /// Called when a client reconnects to prevent automatic game reset
    /// </summary>
    public void PrepareForReconnection()
    {
        // Mark the multiplayer game as active to prevent automatic start
        isMultiplayerGameActive = true;
        
        // Set a flag to skip auto-start on reconnection
        skipAutoStartGame = true;
        
        // Make sure we don't reset the game if the player reconnects
        Debug.Log("NETWORK MANAGER: Prepared for reconnection, will skip automatic game start");
        
        // Subscribe to GameManager event if we need to capture the state immediately 
        // upon reconnection for a client
        if (GameManager.Instance != null)
        {
            Debug.Log("NETWORK MANAGER: GameManager found, will maintain game state during reconnection");
        }
    }
    
    /// <summary>
    /// Forces an update of piece control settings after reconnection
    /// </summary>
    public void ForceUpdatePieceControlOnReconnect()
    {
        Debug.Log("CHESS NETWORK: Force updating piece control settings after reconnection");
        
        // Let a small delay pass to ensure all pieces are properly initialized
        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                Debug.Log("CHESS NETWORK: Delayed control update executing");
                UpdatePieceControl();
            });
        });
    }
    
    // Add these methods to your ChessNetworkManager.cs file

/// <summary>
/// Handles game end conditions and notifies all clients
/// </summary>
public void HandleGameEnd(GameEndReason reason, Side winningSide = Side.None)
{
    if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
    {
        Debug.Log($"GAME END: Game ended due to {reason}, winner: {winningSide}");
        
        // Notify all clients about the game outcome
        NotifyGameEndClientRpc((int)reason, (int)winningSide);
        
        // Set a flag to prevent further moves
        isMultiplayerGameActive = false;
    }
}

/// <summary>
/// Called when a client disconnects to handle it as a potential resignation
/// </summary>
private void HandleDisconnectionAsResignation(ulong clientId)
{
    // Only process this if we're in an active game
    if (!isMultiplayerGameActive) return;
    
    // Determine which side resigned based on who disconnected
    if (clientId == NetworkManager.Singleton.LocalClientId)
    {
        // Local player disconnected - handled by the other player's instance
        return;
    }
    
    // Get the side of the disconnected player
    Side disconnectedSide = GetPlayerSide(clientId);
    
    if (disconnectedSide != Side.None)
    {
        // The disconnected player's side loses, the other side wins
        Side winningSide = disconnectedSide == Side.White ? Side.Black : Side.White;
        
        Debug.Log($"RESIGNATION: Player {clientId} with side {disconnectedSide} has disconnected, treating as resignation");
        
        // Handle as a resignation
        HandleGameEnd(GameEndReason.Resignation, winningSide);
    }
}

/// <summary>
/// Sends game end notification to all clients
/// </summary>
[ClientRpc]
public void NotifyGameEndClientRpc(int reasonValue, int winningSideValue)
{
    GameEndReason reason = (GameEndReason)reasonValue;
    Side winningSide = (Side)winningSideValue;
    
    Debug.Log($"GAME END NOTIFICATION: Game ended due to {reason}, winner: {winningSide}");
    
    // Display appropriate message in UI
    string endMessage = "";
    
    switch (reason)
    {
        case GameEndReason.Checkmate:
            endMessage = $"{winningSide} Wins by Checkmate!";
            break;
        case GameEndReason.Stalemate:
            endMessage = "Game Drawn by Stalemate!";
            break;
        case GameEndReason.Resignation:
            endMessage = $"{winningSide} Wins by Resignation!";
            break;
        case GameEndReason.Disconnection:
            endMessage = $"{winningSide} Wins by Disconnection!";
            break;
    }
    
    // Update the UI
    if (UIManager.Instance != null)
    {
        UIManager.Instance.ShowGameEndMessage(endMessage);
    }
    
    // Broadcast status message
    BroadcastStatus($"GAME OVER: {endMessage}");
    
    // Disable all piece interaction
    if (BoardManager.Instance != null)
    {
        BoardManager.Instance.SetActiveAllPieces(false);
    }
}

/// <summary>
/// Call this method to handle resignation when a player clicks a resign button
/// </summary>
public void ResignGame()
{
    if (!isMultiplayerGameActive) return;
    
    // Get the side of the local player
    Side localSide = GetPlayerSide(NetworkManager.Singleton.LocalClientId);
    
    // The other side wins
    Side winningSide = localSide == Side.White ? Side.Black : Side.White;
    
    Debug.Log($"RESIGNATION: Local player with side {localSide} has resigned");
    
    // Notify the server of resignation
    ResignGameServerRpc(NetworkManager.Singleton.LocalClientId);
}

/// <summary>
/// Server RPC to handle resignation
/// </summary>
[ServerRpc(RequireOwnership = false)]
private void ResignGameServerRpc(ulong resigningClientId)
{
    Side resigningSide = GetPlayerSide(resigningClientId);
    Side winningSide = resigningSide == Side.White ? Side.Black : Side.White;
    
    // Handle as a resignation
    HandleGameEnd(GameEndReason.Resignation, winningSide);
}

// Add this enum to define game end reasons
public enum GameEndReason
{
    Checkmate,
    Stalemate,
    Resignation,
    Disconnection
}
}