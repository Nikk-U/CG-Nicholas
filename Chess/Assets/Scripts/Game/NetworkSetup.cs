using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Sets up networking components for the chess game.
/// This script avoids adding NetworkObjects to individual squares.
/// </summary>
public class NetworkSetup : MonoBehaviour
{
    [SerializeField] private GameObject chessBoard;

    private void Start()
    {
        // Only set up minimal networking, avoiding NetworkObjects on individual squares
        SetupMinimalNetworking();
    }

    private void SetupMinimalNetworking()
    {
        Debug.Log("NETWORK BOOTSTRAP: Initializing minimal networking architecture for chess game");

        // Find chessBoard if not assigned
        if (chessBoard == null)
        {
            chessBoard = GameObject.FindGameObjectWithTag("Board");
            if (chessBoard == null)
            {
                // Try to find by name
                chessBoard = GameObject.Find("Board");
            }
        }

        if (chessBoard != null)
        {
            Debug.Log("BOARD LOCATED: Chess board successfully identified for network configuration");

            // Ensure the board itself has a NetworkObject
            if (chessBoard.GetComponent<NetworkObject>() == null)
            {
                NetworkObject boardNetObj = chessBoard.AddComponent<NetworkObject>();
                boardNetObj.DontDestroyWithOwner = true;
                Debug.Log("COMPONENT ADDED: NetworkObject attached to chess board with persistence enabled");
            }

            // DO NOT add NetworkObjects to individual squares as it causes conflicts

            // Make sure there's a ChessMoveRelay in the scene
            SetupChessMoveRelay();
        }
        else
        {
            Debug.LogWarning("REFERENCE MISSING: Unable to locate chess board for network setup");
        }
    }

    private void SetupChessMoveRelay()
    {
        // Find or create ChessMoveRelay
        ChessRelay relay = FindObjectOfType<ChessRelay>();
        if (relay == null)
        {
            GameObject relayObj = new GameObject("ChessMoveRelay");
            relay = relayObj.AddComponent<ChessRelay>();
            Debug.Log("RELAY CREATED: New ChessRelay instance generated for move synchronization");

            // Set the chess board reference
            relay.gameObject.AddComponent<NetworkObject>();
            Debug.Log("RELAY CONFIGURED: NetworkObject component attached to ChessRelay");
        }
        else
        {
            Debug.Log("RELAY DETECTED: Using existing ChessRelay for network communication");
        }
    }
}