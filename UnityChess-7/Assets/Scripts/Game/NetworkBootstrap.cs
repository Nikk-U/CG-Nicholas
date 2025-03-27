using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Bootstraps the networking components for the chess game.
/// This script ensures all required network components are properly initialized.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    // Direct reference to the NetworkManager
    [SerializeField] private NetworkManager networkManager;

    private void Awake()
    {
        // Find the NetworkManager if not assigned directly
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
        }

        // Ensure we have the NetworkManager
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager not found in scene! Make sure to add one.");
        }
        else
        {
            Debug.Log("NetworkManager found. Bootstrap successful.");
        }

        // Ensure we have the ChessNetworkManager
        if (ChessNetworkManager.Instance == null)
        {
            Debug.LogWarning("ChessNetworkManager not found! Creating one...");
            GameObject chessNetObj = new GameObject("ChessNetworkManager");
            chessNetObj.AddComponent<NetworkObject>();
            chessNetObj.AddComponent<ChessNetworkManager>();
        }
    }
}