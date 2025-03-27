using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Monitors network performance metrics like ping/latency between server and clients.
/// Attach this to an empty GameObject in your scene.
/// </summary>
public class NetworkPerformanceMonitor : NetworkBehaviour
{
    // Optional UI text elements to display metrics
    [Header("UI References (Optional)")]
    [SerializeField] private Text pingText;
    [SerializeField] private Text bandwidthText;
    [SerializeField] private Text packetLossText;

    [Header("Monitoring Settings")]
    [SerializeField] private float pingInterval = 1.0f; // How often to measure ping in seconds
    [SerializeField] private int pingHistorySize = 10;  // Number of ping measurements to keep for averaging
    [SerializeField] private bool logToConsole = true;  // Whether to log metrics to the console

    // Ping history for calculating average
    private Queue<float> pingHistory = new Queue<float>();
    
    // Timestamp for ping measurement
    private Dictionary<ulong, float> pingStartTimes = new Dictionary<ulong, float>();
    
    // Last calculated values
    private float currentPing = 0;
    private float averagePing = 0;
    private float minPing = float.MaxValue;
    private float maxPing = 0;
    
    // For bandwidth calculation
    private long lastBytesSent = 0;
    private long lastBytesReceived = 0;
    private float uploadBandwidth = 0;
    private float downloadBandwidth = 0;

    private void Start()
    {
        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }
        
        // Start ping measurements when the network is connected
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        // Wait until network is initialized
        yield return new WaitUntil(() => NetworkManager.Singleton.IsConnectedClient);
        
        // Start ping measurements
        if (IsServer || IsHost)
        {
            // Server/host monitors all clients
            StartCoroutine(ServerPingRoutine());
        }
        else
        {
            // Clients just monitor their connection to the server
            StartCoroutine(ClientPingRoutine());
        }
        
        // Start bandwidth monitoring
        StartCoroutine(BandwidthMonitoringRoutine());
    }

    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }
        
        // Stop all coroutines
        StopAllCoroutines();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"PERFORMANCE MONITOR: Client {clientId} connected, beginning performance tracking");
    }

    private void OnClientDisconnect(ulong clientId)
    {
        Debug.Log($"PERFORMANCE MONITOR: Client {clientId} disconnected, stopping performance tracking");
        pingStartTimes.Remove(clientId);
    }

    #region Server-Side Methods

    private IEnumerator ServerPingRoutine()
    {
        Debug.Log("PERFORMANCE MONITOR: Server ping monitoring started");
        
        while (NetworkManager.Singleton.IsConnectedClient)
        {
            // Ping all connected clients
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.Singleton.LocalClientId || IsHost)
                {
                    // Record start time
                    pingStartTimes[clientId] = Time.realtimeSinceStartup;
                    
                    // Send ping to client
                    PingClientRpc(clientId);
                }
            }
            
            // Wait before next ping cycle
            yield return new WaitForSeconds(pingInterval);
        }
    }

    [ClientRpc]
    private void PingClientRpc(ulong clientIdToPing)
    {
        // Only respond if this is the targeted client
        if (clientIdToPing == NetworkManager.Singleton.LocalClientId)
        {
            // Immediately send pong back to server
            PongServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PongServerRpc(ulong respondingClientId)
    {
        // Calculate round-trip time
        if (pingStartTimes.TryGetValue(respondingClientId, out float startTime))
        {
            float pingTime = (Time.realtimeSinceStartup - startTime) * 1000f; // Convert to milliseconds
            
            // Update ping statistics
            UpdatePingStatistics(pingTime, respondingClientId);
        }
    }

    #endregion

    #region Client-Side Methods

    private IEnumerator ClientPingRoutine()
    {
        Debug.Log("PERFORMANCE MONITOR: Client ping monitoring started");
        
        while (NetworkManager.Singleton.IsConnectedClient)
        {
            // Record start time
            float startTime = Time.realtimeSinceStartup;
            
            // Send ping to server
            PingServerRpc(NetworkManager.Singleton.LocalClientId);
            
            // Store start time
            pingStartTimes[NetworkManager.Singleton.LocalClientId] = startTime;
            
            // Wait before next ping
            yield return new WaitForSeconds(pingInterval);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PingServerRpc(ulong clientId)
    {
        // Immediately respond with pong
        PongClientRpc(clientId);
    }

    [ClientRpc]
    private void PongClientRpc(ulong targetClientId)
    {
        // Only process if this is the targeted client
        if (targetClientId == NetworkManager.Singleton.LocalClientId)
        {
            // Calculate round-trip time
            if (pingStartTimes.TryGetValue(NetworkManager.Singleton.LocalClientId, out float startTime))
            {
                float pingTime = (Time.realtimeSinceStartup - startTime) * 1000f; // Convert to milliseconds
                
                // Update ping statistics
                UpdatePingStatistics(pingTime, NetworkManager.Singleton.LocalClientId);
            }
        }
    }

    #endregion

    #region Statistics and Monitoring

    private void UpdatePingStatistics(float pingTime, ulong clientId)
    {
        // Update current ping
        currentPing = pingTime;
        
        // Add to history
        pingHistory.Enqueue(pingTime);
        
        // Keep history at desired size
        while (pingHistory.Count > pingHistorySize)
        {
            pingHistory.Dequeue();
        }
        
        // Calculate average
        float sum = 0;
        foreach (float ping in pingHistory)
        {
            sum += ping;
        }
        averagePing = sum / pingHistory.Count;
        
        // Update min/max
        minPing = Mathf.Min(minPing, pingTime);
        maxPing = Mathf.Max(maxPing, pingTime);
        
        // Log the ping data
        if (logToConsole)
        {
            string role = IsServer ? "Server" : "Client";
            if (IsHost) role = "Host";
            
            string targetName = IsServer ? $"Client {clientId}" : "Server";
            if (clientId == NetworkManager.Singleton.LocalClientId && IsHost) targetName = "Self (Host)";
            
            Debug.Log($"PERFORMANCE: [{role}] Ping to {targetName}: {pingTime:F1}ms (Avg: {averagePing:F1}ms, Min: {minPing:F1}ms, Max: {maxPing:F1}ms)");
        }
        
        // Update UI if available
        UpdateUI();
    }

    private IEnumerator BandwidthMonitoringRoutine()
    {
        Debug.Log("PERFORMANCE MONITOR: Bandwidth monitoring started");
        
        // Get initial metrics
        long initialBytesSent = 0;
        long initialBytesReceived = 0;
        
        // Check if Netcode for GameObjects exposes these metrics
        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != null)
        {
            // Some transports may provide these stats
            // For example with UTP: NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetBatchedBytesSent();
            // This would need to be extended depending on which transport you're using
            
            lastBytesSent = initialBytesSent;
            lastBytesReceived = initialBytesReceived;
        }
        
        while (NetworkManager.Singleton.IsConnectedClient)
        {
            // Wait for interval
            yield return new WaitForSeconds(1.0f);
            
            // Get current metrics
            long currentBytesSent = 0;
            long currentBytesReceived = 0;
            
            // Calculate bandwidth (bytes per second)
            uploadBandwidth = (currentBytesSent - lastBytesSent);
            downloadBandwidth = (currentBytesReceived - lastBytesReceived);
            
            // Update last values
            lastBytesSent = currentBytesSent;
            lastBytesReceived = currentBytesReceived;
            
            // Log bandwidth
            if (logToConsole)
            {
                // Detecting packet loss reliably is complex and transport-dependent
                string packetLossInfo = "N/A - Enable in transport settings";
                
                Debug.Log($"PERFORMANCE: Bandwidth - Upload: {FormatBandwidth(uploadBandwidth)}, Download: {FormatBandwidth(downloadBandwidth)}, Packet Loss: {packetLossInfo}");
            }
            
            // Update UI
            UpdateUI();
        }
    }

    private string FormatBandwidth(float bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F1} B/s";
        else if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F1} KB/s";
        else
            return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
    }

    private void UpdateUI()
    {
        // Update UI if references are set
        if (pingText != null)
        {
            pingText.text = $"Ping: {currentPing:F1}ms (Avg: {averagePing:F1}ms)";
        }
        
        if (bandwidthText != null)
        {
            bandwidthText.text = $"Bandwidth: ↑{FormatBandwidth(uploadBandwidth)} ↓{FormatBandwidth(downloadBandwidth)}";
        }
        
        if (packetLossText != null)
        {
            packetLossText.text = "Packet Loss: Not Available";
        }
    }

    #endregion
}