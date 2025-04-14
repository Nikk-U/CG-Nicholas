using System;
using Unity.Netcode;
using UnityEngine;
using UnityChess;

namespace Data
{
    
    public static class ChessRelayExtension
    {
        /// <summary>
        /// Synchronize game state across all clients and host
        /// </summary>
        /// <param name="relay">The ChessRelay instance</param>
        /// <param name="gameState">The serialized game state</param>
        [ClientRpc]
        public static void SyncGameStateClientRpc(this ChessRelay relay, string gameState)
        {
            Debug.Log($"SYNC: Attempting to load game state. IsHost: {NetworkManager.Singleton.IsHost}, IsServer: {NetworkManager.Singleton.IsServer}");
            
            try
            {
                // Load the game from the serialized state
                GameManager.Instance.LoadGame(gameState);
                
                // Update board visuals
                BoardManager.Instance.OnGameResetToHalfMove();
                
                // Update piece control in multiplayer
                if (ChessNetworkManager.Instance != null)
                {
                    ChessNetworkManager.Instance.UpdatePieceControl();
                }
                
                Debug.Log("SYNC: Successfully synchronized game state");
            }
            catch (Exception ex)
            {
                Debug.LogError($"SYNC ERROR: Failed to synchronize game state - {ex.Message}");
                Debug.LogError($"Full Exception: {ex.StackTrace}");
            }
        }
    
        /// <summary>
        /// Request game state from the host
        /// </summary>
        /// <param name="relay">The ChessRelay instance</param>
        [ServerRpc(RequireOwnership = false)]
        public static void RequestGameStateServerRpc(this ChessRelay relay)
        {
            // Ensure only host/server responds
            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("SYNC: Non-host attempting to request game state");
                return;
            }
            
            Debug.Log("SYNC: Received game state request from client");
            
            // Get current game state
            string gameState = GameManager.Instance.SerializeGame();
            
            // Send to ALL clients including host
            relay.SyncGameStateClientRpc(gameState);
            
            Debug.Log("SYNC: Sent game state to all clients");
        }
    
        /// <summary>
        /// Synchronize a loaded game state with all clients and host
        /// </summary>
        /// <param name="relay">The ChessRelay instance</param>
        /// <param name="gameState">The serialized game state</param>
        [ServerRpc(RequireOwnership = false)]
        public static void SynchronizeLoadedGameStateServerRpc(this ChessRelay relay, string gameState)
        {
            // Ensure only host/server can synchronize
            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("SYNC: Non-host attempting to synchronize game state");
                return;
            }
            
            Debug.Log("SYNC: Synchronizing loaded game state with all clients");
            
            // Send the state to ALL clients
            relay.SyncGameStateClientRpc(gameState);
        }
    
        /// <summary>
        /// Helper method to save and synchronize game state across the network
        /// </summary>
        /// <param name="relay">The ChessRelay instance</param>
        public static void SaveAndSyncGameState(this ChessRelay relay)
        {
            // Only the host can initiate state synchronization
            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("SYNC: Non-host attempting to save and sync game state");
                return;
            }
            
            // Serialize current game state
            string gameState = GameManager.Instance.SerializeGame();
            
            // Synchronize the game state across the network
            relay.SynchronizeLoadedGameStateServerRpc(gameState);
        }
        
        /// <summary>
        /// Manually trigger game state synchronization for the host
        /// </summary>
        /// <param name="relay">The ChessRelay instance</param>
        public static void ForceHostGameStateSync(this ChessRelay relay)
        {
            if (!NetworkManager.Singleton.IsHost)
            {
                Debug.LogWarning("SYNC: Only host can force game state sync");
                return;
            }
            
            // Get current game state
            string gameState = GameManager.Instance.SerializeGame();
            
            // Directly load the game state on the host
            GameManager.Instance.LoadGame(gameState);
            BoardManager.Instance.OnGameResetToHalfMove();
            
            // Sync to all clients
            relay.SyncGameStateClientRpc(gameState);
            
            Debug.Log("SYNC: Forced host game state synchronization");
        }

        /// <summary>
        /// Server RPC to notify about a skin purchase or equip
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public static void NotifySkinPurchasedServerRpc(this ChessRelay relay, string playerId, string skinId)
        {
            Debug.Log($"SERVER: Received skin update for {skinId} from client {NetworkManager.Singleton.LocalClientId}");
            
            // Check if this is from the host
            bool fromHost = NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId;
            
            // Get the sender's ID for filtering
            ulong senderId = NetworkManager.Singleton.LocalClientId;
            
            Debug.Log($"SERVER: Processing skin update. FromHost={fromHost}, SenderId={senderId}");
            
            // Broadcast to all clients
            relay.NotifySkinPurchasedClientRpc(playerId, skinId, fromHost, senderId);
        }

        /// <summary>
        /// Client RPC to notify all clients about a skin purchase or equip
        /// </summary>
        [ClientRpc]
        public static void NotifySkinPurchasedClientRpc(this ChessRelay relay, string playerId, string skinId, bool fromHost, ulong senderId)
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
            
            // Record the skin purchase in Firebase
            if (FirebaseAnalyticsManager.Instance != null)
            {
                Debug.Log($"CLIENT: Recording skin purchase for {playerId}, Skin: {skinId}");
                FirebaseAnalyticsManager.Instance.RecordSkinPurchase(playerId, skinId);
            }
            else
            {
                Debug.LogError("Firebase Analytics Manager not found for skin update");
            }
        }
    }
}