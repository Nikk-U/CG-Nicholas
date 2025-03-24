using System;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;

[DefaultExecutionOrder(100)]
public class PlayerConnectionManager : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button serverButton;

    // Example: track connected clients count
    [SerializeField] private TMP_Text infoText;

    public event Action<string> ConnectionStatusChanged;

    private void Awake()
    {
        // If you have UI Buttons, hook them up to the methods below
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (serverButton != null) serverButton.onClick.AddListener(StartServer);

        // Register for Netcode callbacks if needed
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
    }

    private void OnDestroy()
    {
        // Clean up callbacks to avoid issues if this object is destroyed
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

    /// <summary>
    /// Start as Host (acts as both Server and Client).
    /// </summary>
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        UpdateInfoText("Starting as Host...");
    }

    /// <summary>
    /// Start as Server only (no local client).
    /// </summary>
    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        UpdateInfoText("Starting as Server...");
    }

    /// <summary>
    /// Start as a Client. Make sure the NetworkManager’s transport settings (IP, port, etc.)
    /// are set appropriately, or you’re using Relay, etc.
    /// </summary>
    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        UpdateInfoText("Starting as Client...");
    }

    // --------------------- Callbacks ---------------------

    private void OnServerStarted()
    {
        // This fires on the server/host when it has fully started.
        if (NetworkManager.Singleton.IsHost)
        {
            UpdateInfoText("Host started. You are also a client.");
        }
        else
        {
            UpdateInfoText("Server started. No local client.");
        }
        if (NetworkManager.Singleton.IsServer)
        {
            // Ensure GameManager.Instance is not null.
            GameManager.Instance.StartNewGame();
            Debug.Log("New game started on server.");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        string status = clientId == NetworkManager.Singleton.LocalClientId ? "Host connected." : $"Client {clientId} connected.";
        Debug.Log(status);
        ConnectionStatusChanged?.Invoke(status);
        UpdateInfoText(status);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        string status = clientId == NetworkManager.Singleton.LocalClientId ? "Host disconnected." : $"Client {clientId} disconnected.";
        Debug.Log(status);
        ConnectionStatusChanged?.Invoke(status);
        UpdateInfoText(status);
    }

    private void OnTransportFailure()
    {
        string status = "Transport failure occurred.";
        Debug.LogError(status);
        ConnectionStatusChanged?.Invoke(status);
        UpdateInfoText(status);
    }

    // --------------------- Session Management ---------------------

    public void JoinSession(string sessionCode)
    {
        if (IsValidSessionCode(sessionCode))
        {
            NetworkManager.Singleton.StartClient();
            string status = "Client joined the session.";
            Debug.Log(status);
            ConnectionStatusChanged?.Invoke(status);
            UpdateInfoText(status);
        }
        else
        {
            Debug.LogError("Invalid session code.");
        }
    }

    public void LeaveSession()
    {
        if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
            string status = "Client left the session.";
            Debug.Log(status);
            ConnectionStatusChanged?.Invoke(status);
            UpdateInfoText(status);
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
            string status = "Host left the session.";
            Debug.Log(status);
            ConnectionStatusChanged?.Invoke(status);
            UpdateInfoText(status);
        }
    }

    public void RejoinSession()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.StartHost();
            string status = "Host rejoined the session.";
            Debug.Log(status);
            ConnectionStatusChanged?.Invoke(status);
            UpdateInfoText(status);
        }
        else if (!NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartClient();
            string status = "Client rejoined the session.";
            Debug.Log(status);
            ConnectionStatusChanged?.Invoke(status);
            UpdateInfoText(status);
        }
    }

    private bool IsValidSessionCode(string sessionCode)
    {
        return !string.IsNullOrEmpty(sessionCode);
    }

    // --------------------- Helpers ---------------------

    private void UpdateInfoText(string message)
    {
        if (infoText != null)
        {
            infoText.text = message;
        }
        Debug.Log(message);
    }

    private int GetConnectedCount()
    {
        // The server has a dictionary of connected clients
        return NetworkManager.Singleton.ConnectedClients.Count;
    }
}