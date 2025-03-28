using UnityEngine;
using UnityChess;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Preserves the visual board state for hosts during client reconnection
/// This component acts as a direct protector against state resets
/// </summary>
public class BoardStatePreserver : MonoBehaviour
{
    // Static instance for global access
    public static BoardStatePreserver Instance { get; private set; }
    
    // Structure to store a piece position
    private class PieceState
    {
        public GameObject PieceObject;
        public Transform SquareTransform;
        public Side PieceSide;
    }
    
    // Flag to indicate we're currently in protected mode (preventing resets)
    private bool isProtectingState = false;
    
    // Store the original piece locations
    private List<PieceState> savedPieceStates = new List<PieceState>();
    
    // Track if the game has started (pieces have been set up)
    private bool gameHasStarted = false;
    
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
        // Subscribe to relevant events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnect;
        }
        
        // Subscribe to ChessNetworkManager events
        ChessNetworkManager.OnNetworkStatusChanged += OnChessNetworkStatusChanged;
        
        // Subscribe to GameManager events
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnect;
        }
        
        ChessNetworkManager.OnNetworkStatusChanged -= OnChessNetworkStatusChanged;
        
        GameManager.NewGameStartedEvent -= OnNewGameStarted;
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
    }
    
    private void OnNewGameStarted()
    {
        gameHasStarted = true;
        
        // Start tracking the game after a short delay to let the board initialize
        StartCoroutine(DelayedCapture());
    }
    
    private IEnumerator DelayedCapture()
    {
        // Wait for board setup to complete
        yield return new WaitForSeconds(1.0f);
        
        // Capture initial state
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            CaptureBoardState();
        }
    }
    
    private void OnMoveExecuted()
    {
        // Update our saved state after each move if we're the host
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            CaptureBoardState();
        }
    }
    
    private void OnClientDisconnect(ulong clientId)
    {
        // When a client disconnects, capture the current state if we're the host
        if ((NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) 
            && clientId != NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("BOARD PRESERVER: Client disconnected, capturing current board state");
            CaptureBoardState();
        }
    }
    
    private void OnClientConnect(ulong clientId)
    {
        // When a client connects and we're the host, enable protection
        if ((NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) 
            && clientId != NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("BOARD PRESERVER: Client connected, enabling board protection");
            
            // Start protection coroutine
            StartCoroutine(ProtectBoardState());
        }
    }
    
    private void OnChessNetworkStatusChanged(string status)
    {
        // Watch for messages that indicate client reconnection
        if ((status.Contains("reconnect") || status.Contains("Reconnect")) 
            && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
        {
            Debug.Log("BOARD PRESERVER: Detected reconnection message, enabling board protection");
            StartCoroutine(ProtectBoardState());
        }
    }
    
    private void CaptureBoardState()
    {
        if (!gameHasStarted || !NetworkManager.Singleton.IsConnectedClient)
            return;
            
        // Clear previous state
        savedPieceStates.Clear();
        
        // Find all visual pieces on the board
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>(true);
        
        foreach (VisualPiece piece in allPieces)
        {
            // Store piece state
            PieceState state = new PieceState
            {
                PieceObject = piece.gameObject,
                SquareTransform = piece.transform.parent,
                PieceSide = piece.PieceColor
            };
            
            savedPieceStates.Add(state);
            
            Debug.Log($"BOARD PRESERVER: Saved piece {piece.name} at square {state.SquareTransform.name}");
        }
        
        Debug.Log($"BOARD PRESERVER: Captured state of {savedPieceStates.Count} pieces");
    }
    
    private IEnumerator ProtectBoardState()
    {
        // This flag prevents restoring the state while we're in protection mode
        isProtectingState = true;
        
        // Wait for state changes that might occur during reconnection
        for (int i = 0; i < 20; i++)
        {
            // Check every 0.1 seconds
            yield return new WaitForSeconds(0.1f);
            
            // If we're the host, restore any pieces that moved unexpectedly
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                RestoreBoardState();
            }
        }
        
        isProtectingState = false;
        Debug.Log("BOARD PRESERVER: Protection period ended");
    }
    
    private void RestoreBoardState()
    {
        if (!gameHasStarted || savedPieceStates.Count == 0)
            return;
            
        Debug.Log("BOARD PRESERVER: Checking board state...");
        
        foreach (PieceState state in savedPieceStates)
        {
            if (state.PieceObject != null)
            {
                // Only restore if the piece isn't already at the correct square
                if (state.PieceObject.transform.parent != state.SquareTransform)
                {
                    Debug.Log($"BOARD PRESERVER: Restoring piece {state.PieceObject.name} to square {state.SquareTransform.name}");
                    
                    // Reparent the piece to its saved square
                    state.PieceObject.transform.parent = state.SquareTransform;
                    state.PieceObject.transform.localPosition = Vector3.zero;
                }
            }
        }
    }
    
    // Public method to force an immediate restore (can be called from other scripts)
    public void ForceRestoreBoardState()
    {
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            Debug.Log("BOARD PRESERVER: Force restoring board state");
            RestoreBoardState();
        }
    }
}