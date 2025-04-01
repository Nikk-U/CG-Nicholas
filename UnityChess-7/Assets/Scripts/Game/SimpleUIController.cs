using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityChess;
using System.Collections;

/// <summary>
/// Simplified UI controller with just the basic connection buttons
/// Enhanced with turn management capabilities
/// </summary>
public class SimpleUIController : MonoBehaviour
{
    [Header("Connection Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button rejoinButton;
    [SerializeField] private Button leaveButton;
    [Header("Game Control")]
    [SerializeField] private Button resignButton;
    
    [Header("Status")]
    [SerializeField] private Text statusText;
    
    // Save the last connection info
    private string lastConnectedIp = "127.0.0.1";
    private string lastGameState = "";
    private int lastMoveIndex = 0;
    private bool isReconnecting = false;
    
    // Flag to prevent multiple restoration attempts
    private bool isRestoringState = false;
    
    // Store a reference to active coroutines to be able to stop them
    private Coroutine stateRestorationCoroutine = null;
    
    // Track if we were the host before disconnecting (static so GameStatePersistence can access it)
    public static bool WasHostBeforeDisconnect { get; private set; } = false;
    
    // Flag to indicate the game should skip GameManager.LoadGame
    private bool skipStateRestoration = false;
    
    // Flag to control turn notifications
    private bool isTurnNotificationEnabled = true;
    
    private void Start()
    {
        // Set button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostClicked);
            
        if (clientButton != null)
            clientButton.onClick.AddListener(OnClientClicked);
            
        if (rejoinButton != null)
            rejoinButton.onClick.AddListener(OnRejoinClicked);
            
        if (leaveButton != null)
            leaveButton.onClick.AddListener(OnLeaveClicked);
        
        if (resignButton != null)
            resignButton.onClick.AddListener(OnResignClicked);

            
        // Initialize button states
        UpdateButtonStates(false);
        
        // Set initial status
        UpdateStatus("Ready to connect. Click Host or Join.");
        
        // Subscribe to chess network status updates
        ChessNetworkManager.OnNetworkStatusChanged += OnChessNetworkStatusChanged;
        
        // Subscribe to game events to capture the game state
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        
        // Subscribe to GameManager events for turn notifications
        if (GameManager.Instance != null)
        {
            GameManager.MoveExecutedEvent += OnMoveExecutedForTurnUpdate;
            GameManager.NewGameStartedEvent += OnNewGameStartedForTurnUpdate;
        }
        
        // Initialize static flag
        WasHostBeforeDisconnect = false;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from chess network status updates
        ChessNetworkManager.OnNetworkStatusChanged -= OnChessNetworkStatusChanged;
        
        // Unsubscribe from game events
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        // Unsubscribe from GameManager events for turn notifications
        if (GameManager.Instance != null)
        {
            GameManager.MoveExecutedEvent -= OnMoveExecutedForTurnUpdate;
            GameManager.NewGameStartedEvent -= OnNewGameStartedForTurnUpdate;
        }
        
        // Stop any active coroutines
        if (stateRestorationCoroutine != null)
            StopCoroutine(stateRestorationCoroutine);
    }
    
    private void OnMoveExecuted()
    {
        // Skip if we're in the process of restoring
        if (isRestoringState)
            return;
            
        // If this is the host, save the current game state after each move
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            CaptureCurrentGameState();
        }
    }
    
    /// <summary>
    /// Handle the move executed event for turn updates
    /// </summary>
    private void OnMoveExecutedForTurnUpdate()
    {
        if (!isTurnNotificationEnabled || GameManager.Instance == null) 
            return;
            
        // Get the current side to move
        Side currentSide = GameManager.Instance.SideToMove;
        
        // Update the UI with turn notification
        UpdateTurnNotification(currentSide);
    }
    
    /// <summary>
    /// Handle new game started for turn reset
    /// </summary>
    private void OnNewGameStartedForTurnUpdate()
    {
        if (!isTurnNotificationEnabled)
            return;
            
        // Chess always starts with White
        UpdateTurnNotification(Side.White);
    }
    
    /// <summary>
    /// Update the status text with turn notification
    /// </summary>
    private void UpdateTurnNotification(Side sideToMove)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
            return;
            
        string turnMessage;
        
        if (NetworkManager.Singleton.IsHost)
        {
            // Host plays as White
            if (sideToMove == Side.White)
            {
                turnMessage = "YOUR TURN: White to move (You)";
            }
            else
            {
                turnMessage = "WAITING: Black to move (Opponent)";
            }
        }
        else
        {
            // Client plays as Black
            if (sideToMove == Side.Black)
            {
                turnMessage = "YOUR TURN: Black to move (You)";
            }
            else
            {
                turnMessage = "WAITING: White to move (Opponent)";
            }
        }
        
        // Use your existing UpdateStatus method to show the message
        UpdateStatus(turnMessage);
    }
    
    private void CaptureCurrentGameState()
    {
        if (GameManager.Instance != null)
        {
            // Save current game state as a string
            lastGameState = GameManager.Instance.SerializeGame();
            lastMoveIndex = GameManager.Instance.LatestHalfMoveIndex;
            Debug.Log($"UI: Captured game state at move index {lastMoveIndex}");
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
    
    private void OnHostClicked()
    {
        Debug.Log("Host button clicked");
        
        if (NetworkManager.Singleton == null)
        {
            UpdateStatus("Error: NetworkManager not found");
            return;
        }
        
        // Set flag that we're the host
        WasHostBeforeDisconnect = true;
        skipStateRestoration = false;
        
        // Set up the transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
        }
        
        // Reset reconnection flag
        isReconnecting = false;
        
        // Start hosting
        if (NetworkManager.Singleton.StartHost())
        {
            UpdateStatus("Starting host...");
            UpdateButtonStates(true);
            
            // Initialize ChessNetworkManager if needed
            if (ChessNetworkManager.Instance != null)
            {
                ChessNetworkManager.Instance.InitializeAsHost();
            }
            
            // Capture initial game state
            CaptureCurrentGameState();
        }
        else
        {
            UpdateStatus("Failed to start host");
        }
    }
    
    private void OnClientClicked()
    {
        Debug.Log("Client button clicked");
        
        if (NetworkManager.Singleton == null)
        {
            UpdateStatus("Error: NetworkManager not found");
            return;
        }
        
        // Set flag that we're not the host
        WasHostBeforeDisconnect = false;
        skipStateRestoration = false;
        
        // Reset reconnection flag
        isReconnecting = false;
        
        // Use localhost by default
        string ipAddress = "127.0.0.1";
        
        // Set up the transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = ipAddress;
            transport.ConnectionData.Port = 7777;
        }
        
        // Start client
        if (NetworkManager.Singleton.StartClient())
        {
            lastConnectedIp = ipAddress;
            UpdateStatus("Connecting to server...");
            UpdateButtonStates(true);
        }
        else
        {
            UpdateStatus("Failed to connect to server");
        }
    }
    
    private void OnRejoinClicked()
    {
        Debug.Log("Rejoin button clicked");
    
        if (NetworkManager.Singleton == null)
        {
            UpdateStatus("Error: NetworkManager not found");
            return;
        }
    
        // Set reconnection flag
        isReconnecting = true;
    
        // Tell ChessNetworkManager to prepare for reconnection
        if (ChessNetworkManager.Instance != null)
        {
            ChessNetworkManager.Instance.PrepareForReconnection();
        }
    
        // Set up the transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = lastConnectedIp;
            transport.ConnectionData.Port = 7777;
        }
    
        // CRITICAL FIX: Handle host vs client differently when rejoining
        if (WasHostBeforeDisconnect)
        {
            // Force skip state restoration for host
            skipStateRestoration = true;
            
            // We were the host previously, so restart as host
            if (NetworkManager.Singleton.StartHost())
            {
                UpdateStatus("Reconnecting as host...");
                UpdateButtonStates(true);
                Debug.Log("UI: Reconnected as host, maintaining current game state");
                
                // Apply extra protection for host
                ApplyHostProtection();
                
                // Force the board preserver to restore the state immediately
                StartCoroutine(DelayedBoardProtection());
            }
            else
            {
                UpdateStatus("Failed to reconnect as host");
            }
        }
        else
        {
            // Allow state restoration for client
            skipStateRestoration = false;
            
            // We were a client previously, so restart as client
            if (NetworkManager.Singleton.StartClient())
            {
                UpdateStatus("Reconnecting to server...");
                UpdateButtonStates(true);
            
                // Only start the restoration coroutine for clients, not for hosts
                if (stateRestorationCoroutine != null)
                    StopCoroutine(stateRestorationCoroutine);
                
                stateRestorationCoroutine = StartCoroutine(DelayedStateRequest());
            }
            else
            {
                UpdateStatus("Failed to reconnect to server");
            }
        }
    }
    
    /// <summary>
    /// Critical override to prevent host state restoration
    /// This directly modifies the LoadGame functionality in GameManager
    /// to protect the host's board state during reconnection
    /// </summary>
    private void ApplyHostProtection()
    {
        // Create the host protection GameObject if it doesn't exist
        if (GameObject.Find("HostBoardStateProtector") == null)
        {
            GameObject protector = new GameObject("HostBoardStateProtector");
            protector.AddComponent<BoardStatePreserver>();
            DontDestroyOnLoad(protector);
            
            Debug.Log("UI: Added board state protection for host");
        }
    }
    
    private IEnumerator DelayedBoardProtection()
    {
        // Wait for board setup to complete
        yield return new WaitForSeconds(0.5f);
        
        // Try to force board state restoration for host
        BoardStatePreserver preserver = FindObjectOfType<BoardStatePreserver>();
        if (preserver != null)
        {
            preserver.ForceRestoreBoardState();
        }
        
        // Try again after another delay
        yield return new WaitForSeconds(0.5f);
        
        preserver = FindObjectOfType<BoardStatePreserver>();
        if (preserver != null)
        {
            preserver.ForceRestoreBoardState();
        }
    }
    
    private void OnLeaveClicked()
    {
        Debug.Log("Leave button clicked");
    
        if (NetworkManager.Singleton == null)
        {
            UpdateStatus("Error: NetworkManager not found");
            return;
        }
    
        // Set a flag to indicate this is a voluntary leave, not a disconnection
        if (ChessNetworkManager.Instance != null)
        {
            ChessNetworkManager.Instance.PlayerIsLeavingVoluntarily = true;
        }
    
        // Remember if we were the host before disconnecting
        WasHostBeforeDisconnect = NetworkManager.Singleton.IsHost;
        Debug.Log($"UI: Saving host status before disconnect: {WasHostBeforeDisconnect}");
    
        // Stop any active restoration
        if (stateRestorationCoroutine != null)
        {
            StopCoroutine(stateRestorationCoroutine);
            stateRestorationCoroutine = null;
        }
    
        isRestoringState = false;
    
        // Clear any win/lose messages
        if (UIManager.Instance != null && UIManager.Instance.resultText != null)
        {
            UIManager.Instance.resultText.gameObject.SetActive(false);
        }
    
        // Shutdown the network connection
        NetworkManager.Singleton.Shutdown();
        UpdateStatus("Disconnected. Ready to connect again.");
        UpdateButtonStates(false);
    }
    
    // Add this method to SimpleUIController.cs to handle reconnection properly
    private IEnumerator DelayedStateRequest()
    {
        Debug.Log("UI: Starting delayed state request for client");

        // SAFETY CHECK: Exit immediately if this is or becomes a host or if it should skip
        if (NetworkManager.Singleton.IsHost || WasHostBeforeDisconnect || skipStateRestoration)
        {
            Debug.Log("UI: This is a host or state restoration is skipped, not requesting state");
            isRestoringState = false;
            stateRestorationCoroutine = null;
            yield break;
        }

        // Wait for connection to be established
        yield return new WaitForSeconds(1.0f);

        // SAFETY CHECK: Exit if this became a host or if it should skip
        if (NetworkManager.Singleton.IsHost || WasHostBeforeDisconnect || skipStateRestoration)
        {
            Debug.Log("UI: This is a host or state restoration is skipped, canceling state request");
            isRestoringState = false;
            stateRestorationCoroutine = null;
            yield break;
        }

        // If we're a client, proceed with requesting state
        if (NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.Log("UI: Client connection established, requesting game state from server");
            isRestoringState = true;
        
            // Clear any game end messages
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HideGameEndMessage();
            }

            // Request the game state
            RequestGameStateServerRpc(NetworkManager.Singleton.LocalClientId);

            // Wait for the response
            yield return new WaitForSeconds(1.0f);

            // Restore the game state (only for client)
            RestoreGameState();

            // Reset the flag
            isRestoringState = false;
        }

        stateRestorationCoroutine = null;
    }

    private void RestoreGameState()
    {
        // CRITICAL: Absolutely never restore state if we're the host or skipping restoration
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || WasHostBeforeDisconnect || skipStateRestoration)
        {
            Debug.Log("UI: PREVENTING HOST/SERVER FROM RESTORING STATE");
            return;
        }
    
        if (string.IsNullOrEmpty(lastGameState))
        {
            Debug.Log("UI: No game state to restore");
            return;
        }
    
        Debug.Log($"UI: Restoring game state for client, length: {lastGameState.Length}, move index: {lastMoveIndex}");
    
        try
        {
            // Extra safety check to ensure we're not the host
            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer && !WasHostBeforeDisconnect && !skipStateRestoration)
            {
                // Load the game from the serialized state
                GameManager.Instance.LoadGame(lastGameState);
            
                // Force board update
                BoardManager.Instance.OnGameResetToHalfMove();
            
                // Update network-specific piece control
                if (ChessNetworkManager.Instance != null)
                {
                    ChessNetworkManager.Instance.UpdatePieceControl();
                }
            
                UpdateStatus($"Game state restored to move {lastMoveIndex}");
                
                // After state restoration, update the turn notification
                if (GameManager.Instance != null)
                {
                    UpdateTurnNotification(GameManager.Instance.SideToMove);
                }
            }
            else
            {
                Debug.LogWarning("UI: Attempted to restore state on host, operation blocked");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error restoring game state: {e.Message}");
            UpdateStatus("Failed to restore game state");
        }
    }
    
    private void OnChessNetworkStatusChanged(string status)
    {
        // If we're currently restoring a state, intercept certain messages
        if (isRestoringState)
        {
            // Ignore game start messages during restoration
            if (status.Contains("MATCH STARTED") || status.Contains("Game is now") || 
                status.Contains("Waiting for game"))
            {
                Debug.Log($"UI: Ignoring status message during state restoration: {status}");
                return;
            }
        }
        
        // Filter out duplicate turn-related messages
        if (status.Contains("TURN:") || status.Contains("YOUR TURN") || 
            status.Contains("WAITING") || status.Contains("move"))
        {
            // This is a turn notification, update normally but don't overwrite our own
            if (!status.Contains("YOUR TURN") && !status.Contains("WAITING"))
            {
                UpdateStatus(status);
            }
        }
        else 
        {
            // Update UI with chess network status for non-turn-related messages
            UpdateStatus(status);
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"UI detected client connected: {clientId}");

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // We connected
            UpdateStatus("Connected to server");
        
            // Clear any game end messages
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HideGameEndMessage();
            }
        
            // If we're a client, we need to wait for White's (host's) move
            if (!NetworkManager.Singleton.IsHost)
            {
                UpdateStatus("WAITING: White to move (Host)");
            }
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            // Someone else connected to us
            UpdateStatus($"Client {clientId} connected. Game ready!");
        
            // Clear any game end messages
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HideGameEndMessage();
            }
        
            // As the host, indicate it's our turn (White)
            UpdateStatus("YOUR TURN: White to move (You)");
    
            // Only capture state if:
            // 1. We don't already have state AND
            // 2. We're in an active game with moves made
            if (string.IsNullOrEmpty(lastGameState) && 
                GameManager.Instance != null && 
                GameManager.Instance.LatestHalfMoveIndex > 0)
            {
                Debug.Log("UI: Capturing game state for reconnecting client");
                CaptureCurrentGameState();
            }
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"UI detected client disconnected: {clientId}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // We disconnected
            UpdateStatus("Disconnected from server");
            UpdateButtonStates(false);
            
            // Stop any active restoration
            if (stateRestorationCoroutine != null)
            {
                StopCoroutine(stateRestorationCoroutine);
                stateRestorationCoroutine = null;
            }
            
            isRestoringState = false;
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            // Someone else disconnected from us
            UpdateStatus($"Client {clientId} disconnected. Waiting for reconnection.");
            
            // Ensure we have the latest state saved
            CaptureCurrentGameState();
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestGameStateServerRpc(ulong requestingClientId)
    {
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
            return;
        
        // Ignore requests from ourselves (the host) to prevent state loop
        if (requestingClientId == NetworkManager.Singleton.LocalClientId &&
            NetworkManager.Singleton.IsHost)
        {
            Debug.Log("UI: Ignoring state request from host to prevent state reset");
            return;
        }
        
        Debug.Log($"UI: Server received game state request from client {requestingClientId}");
        
        // Make sure we have the current state
        if (string.IsNullOrEmpty(lastGameState))
        {
            CaptureCurrentGameState();
        }
        
        // Send game state directly to the requesting client
        SendGameStateClientRpc(requestingClientId, lastGameState, lastMoveIndex);
    }
    
    [ClientRpc]
    private void SendGameStateClientRpc(ulong targetClientId, string gameState, int moveIndex)
    {
        // Only process if we're the target client
        if (targetClientId != NetworkManager.Singleton.LocalClientId)
            return;
        
        // Skip if we're the server/host or if we should skip restoration
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || WasHostBeforeDisconnect || skipStateRestoration)
        {
            Debug.Log("UI: Host received game state but ignoring it to preserve current state");
            return;
        }
        
        // Save the game state (client only)
        lastGameState = gameState;
        lastMoveIndex = moveIndex;
    
        Debug.Log($"UI: Client received game state from server, move index {moveIndex}, length: {gameState.Length}");
    }
    
    private void UpdateButtonStates(bool isConnected)
    {
        if (hostButton != null)
            hostButton.interactable = !isConnected;
            
        if (clientButton != null)
            clientButton.interactable = !isConnected;
            
        if (rejoinButton != null)
            rejoinButton.interactable = !isConnected;
            
        if (leaveButton != null)
            leaveButton.interactable = isConnected;
    }
    
    public void UpdateStatus(string message)
    {
        Debug.Log($"UI status: {message}");
        
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
    
    // Add this method to SimpleUIController.cs

    /// <summary>
    /// Called when the resign button is clicked
    /// </summary>
    public void OnResignClicked()
    {
        Debug.Log("Resign button clicked");
    
        // Check if we're in a network game
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            UpdateStatus("Cannot resign: Not in a networked game");
            return;
        }
    
        // Check if the game is active
        if (ChessNetworkManager.Instance == null || !ChessNetworkManager.Instance.IsInMultiplayerGame())
        {
            UpdateStatus("Cannot resign: Game not active");
            return;
        }
    
        // Call resign on the ChessNetworkManager
        ChessNetworkManager.Instance.ResignGame();
    
        // Update local status
        UpdateStatus("You have resigned the game");
    }
}