using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityChess;

/// <summary>
/// Handles networked chess moves without requiring individual NetworkObjects for all chess pieces.
/// This simplifies the networking model by only synchronizing move data rather than object transforms.
/// </summary>
public class ChessRelay : NetworkBehaviour
{
    public static ChessRelay Instance { get; private set; }

    [SerializeField] public GameObject chessBoard;

    // Flag to prevent recursive move handling
    private bool isHandlingNetworkMove = false;

    private void Awake()
    {
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

    // Start is called when this component is enabled
    private void Start()
    {
        // Listen for local piece moves when this is active
        VisualPiece.VisualPieceMoved += InterceptPieceMove;
    }

    private void OnDestroy()
    {
        // Clean up event subscription
        VisualPiece.VisualPieceMoved -= InterceptPieceMove;
    }

    /// <summary>
    /// Intercepts piece moves to synchronize them over the network
    /// </summary>
    private void InterceptPieceMove(Square movedPieceInitialSquare, Transform movedPieceTransform,
                                    Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        // Skip if we're already handling a network move
        if (isHandlingNetworkMove) return;

        // Only process if we're in a networked game
        if (!NetworkManager.Singleton.IsConnectedClient || !ChessNetworkManager.Instance.IsInMultiplayerGame())
            return;

        // Get the moving piece's side
        VisualPiece visualPiece = movedPieceTransform.GetComponent<VisualPiece>();
        if (visualPiece == null) return;

        Square destinationSquare = new Square(closestBoardSquareTransform.name);

        // Check if local player is allowed to move this piece
        if (!ChessNetworkManager.Instance.CanControlSide(visualPiece.PieceColor))
        {
            Debug.Log($"PERMISSION DENIED: Player attempting to move opponent's piece from {movedPieceInitialSquare} to {destinationSquare}");
            return;
        }

        Debug.Log($"MOVE DETECTED: Player piece moving from {movedPieceInitialSquare} to {destinationSquare}");

        // After a short delay to allow local game logic to process, relay the move if it was valid
        StartCoroutine(DelayedMoveRelay(movedPieceInitialSquare, destinationSquare));
    }

    private IEnumerator DelayedMoveRelay(Square startSquare, Square endSquare)
    {
        // Give local game logic time to process and validate the move
        yield return new WaitForSeconds(0.2f);

        // Check if the move was successful by seeing if the piece is at the destination
        GameObject pieceAtDest = BoardManager.Instance.GetPieceGOAtPosition(endSquare);
        if (pieceAtDest == null)
        {
            // Move was invalid, no need to relay
            Debug.Log("MOVE REJECTED: No piece found at destination, not relaying invalid move");
            yield break;
        }

        // If move was successful, relay it
        RelayValidatedMoveServerRpc(
            startSquare.File,
            startSquare.Rank,
            endSquare.File,
            endSquare.Rank
        );
    }

    /// <summary>
    /// Sends a move from client to server AFTER local validation
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RelayValidatedMoveServerRpc(int startFile, int startRank, int endFile, int endRank)
    {
        // Server receives a move that was already validated locally
        Debug.Log($"SERVER PROCESSING: Received validated move from {startFile},{startRank} to {endFile},{endRank}");

        // Broadcast to all clients except the one that sent it
        RelayValidatedMoveClientRpc(startFile, startRank, endFile, endRank, NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Broadcasts a validated move from server to all clients
    /// </summary>
    [ClientRpc]
    public void RelayValidatedMoveClientRpc(int startFile, int startRank, int endFile, int endRank, ulong sourceClientId)
    {
        // Don't process if we're the originator
        if (sourceClientId == NetworkManager.Singleton.LocalClientId && !IsHost)
            return;

        Debug.Log($"CLIENT RECEIVED: Move data from {startFile},{startRank} to {endFile},{endRank}");

        // Create squares from the coordinates
        Square startSquare = new Square(startFile, startRank);
        Square endSquare = new Square(endFile, endRank);

        // Execute the move in local game logic
        ExecuteRemoteMove(startSquare, endSquare);
    }

    /// <summary>
    /// Executes a move received from the network by commanding the game logic directly
    /// </summary>
    private void ExecuteRemoteMove(Square startSquare, Square endSquare)
    {
        try
        {
            // Set flag to prevent recursive handling
            isHandlingNetworkMove = true;
            Debug.Log($"EXECUTING REMOTE: Move operation from {startSquare} to {endSquare}");

            // Use the standard game flow to execute this move remotely
            // This avoids parenting issues by letting GameManager handle the UI updates
            GameObject pieceGO = BoardManager.Instance.GetPieceGOAtPosition(startSquare);
            if (pieceGO == null)
            {
                Debug.Log($"PIECE NOT FOUND: Unable to locate piece at {startSquare} for remote move");
                return;
            }

            // Get the destination square
            GameObject squareGO = BoardManager.Instance.GetSquareGOByPosition(endSquare);
            if (squareGO == null)
            {
                Debug.Log($"SQUARE NOT FOUND: Unable to locate square at {endSquare} for remote move");
                return;
            }

            // Temporarily unsubscribe from the visual piece moved event to avoid loops
            VisualPiece.VisualPieceMoved -= InterceptPieceMove;

            // Trigger the move through the standard event system
            VisualPiece.VisualPieceMoved?.Invoke(startSquare, pieceGO.transform, squareGO.transform);

            // Re-subscribe to the event
            VisualPiece.VisualPieceMoved += InterceptPieceMove;

            Debug.Log($"MOVE COMPLETE: Successfully executed remote move from {startSquare} to {endSquare}");
        }
        catch (Exception e)
        {
            Debug.Log($"EXECUTION ERROR: Failed to process remote move: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            // Reset flag
            isHandlingNetworkMove = false;
        }
    }
    
    /// <summary>
    /// Server RPC to notify about a skin purchase or equip
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifySkinPurchasedServerRpc(string skinId)
    {
        Debug.Log($"SERVER: Received skin update for {skinId} from client {NetworkManager.Singleton.LocalClientId}");
        
        // Check if this is from the host
        bool fromHost = NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId;
        
        // Get the sender's ID for filtering
        ulong senderId = NetworkManager.Singleton.LocalClientId;
        
        Debug.Log($"SERVER: Processing skin update. FromHost={fromHost}, SenderId={senderId}");
        
        // Broadcast to all clients
        NotifySkinPurchasedClientRpc(skinId, fromHost, senderId);
    }

    /// <summary>
    /// Client RPC to notify all clients about a skin purchase or equip
    /// </summary>
    [ClientRpc]
    public void NotifySkinPurchasedClientRpc(string skinId, bool fromHost, ulong senderId)
    {
        // Check if we're the sender
        bool isSender = NetworkManager.Singleton.LocalClientId == senderId;
        
        Debug.Log($"CLIENT: Received skin notification: {skinId} from " + 
                  $"{(fromHost ? "Host" : "Client")} (ID: {senderId}). " + 
                  $"I am {(isSender ? "the sender" : "not the sender")}");
        
        // Ignore updates from self - we already applied the change locally
        if (isSender)
        {
            Debug.Log("CLIENT: Ignoring skin update from self");
            return;
        }
        
        // Pass to Firebase DLC Manager for handling
        if (FirebaseDLCManager.Instance != null)
        {
            Debug.Log($"CLIENT: Forwarding skin update to Firebase DLC Manager with fromHost={fromHost}");
            FirebaseDLCManager.Instance.HandleNetworkSkinUpdate(skinId, fromHost);
        }
        else
        {
            Debug.LogError("Firebase DLC Manager not found for skin update");
        }
    }
}