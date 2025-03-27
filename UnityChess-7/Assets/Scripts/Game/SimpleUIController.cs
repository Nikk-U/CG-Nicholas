using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Simplified UI controller with just the basic connection buttons
/// </summary>
public class SimpleUIController : MonoBehaviour
{
    [Header("Connection Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button rejoinButton;
    [SerializeField] private Button leaveButton;
    
    [Header("Status")]
    [SerializeField] private Text statusText;
    
    // Save the last connection info
    private string lastConnectedIp = "127.0.0.1";
    
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
            
        // Initialize button states
        UpdateButtonStates(false);
        
        // Set initial status
        UpdateStatus("Ready to connect. Click Host or Join.");
        
        // Subscribe to chess network status updates
        ChessNetworkManager.OnNetworkStatusChanged += OnChessNetworkStatusChanged;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from chess network status updates
        ChessNetworkManager.OnNetworkStatusChanged -= OnChessNetworkStatusChanged;
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
    
    private void OnChessNetworkStatusChanged(string status)
    {
        // Update UI with chess network status
        UpdateStatus(status);
    }
    
    private void OnHostClicked()
    {
        Debug.Log("Host button clicked");
        
        if (NetworkManager.Singleton == null)
        {
            UpdateStatus("Error: NetworkManager not found");
            return;
        }
        
        // Set up the transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
        }
        
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
        
        // Set up the transport
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = lastConnectedIp;
            transport.ConnectionData.Port = 7777;
        }
        
        // Start client
        if (NetworkManager.Singleton.StartClient())
        {
            UpdateStatus("Reconnecting to server...");
            UpdateButtonStates(true);
        }
        else
        {
            UpdateStatus("Failed to reconnect to server");
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
        
        // Shutdown the network connection
        NetworkManager.Singleton.Shutdown();
        UpdateStatus("Disconnected. Ready to connect again.");
        UpdateButtonStates(false);
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"UI detected client connected: {clientId}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // We connected
            UpdateStatus("Connected to server");
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            // Someone else connected to us
            UpdateStatus($"Client {clientId} connected. Game ready!");
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
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            // Someone else disconnected from us
            UpdateStatus($"Client {clientId} disconnected. Waiting for reconnection.");
        }
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
    
    private void UpdateStatus(string message)
    {
        Debug.Log($"UI status: {message}");
        
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}