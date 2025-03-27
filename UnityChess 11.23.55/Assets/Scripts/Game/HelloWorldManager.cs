using Unity.Netcode;
using UnityEngine;

namespace ChessGame
{
    public class HelloWorldManager : NetworkBehaviour
    {
        private NetworkManager m_NetworkManager;

        void Awake()
        {
            m_NetworkManager = GetComponent<NetworkManager>();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));
            if (!m_NetworkManager.IsClient && !m_NetworkManager.IsServer)
            {
                StartButtons();
            }
            else
            {
                StatusLabels();
            }
            GUILayout.EndArea();
        }

        void StartButtons()
        {
            if (GUILayout.Button("Host")) m_NetworkManager.StartHost();
            if (GUILayout.Button("Client")) m_NetworkManager.StartClient();
            if (GUILayout.Button("Server")) m_NetworkManager.StartServer();
        }

        void StatusLabels()
        {
            var mode = m_NetworkManager.IsHost ?
                "Host" : m_NetworkManager.IsServer ? "Server" : "Client";

            GUILayout.Label("Transport: " +
                m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
            GUILayout.Label("Mode: " + mode);
        }

        // Example move request (call when a player makes a move)
        public void PlayerMoved(string fromSquare, string toSquare)
        {
            SubmitMoveServerRpc(fromSquare, toSquare);
        }

        // Send move to server
        [ServerRpc(RequireOwnership = false)]
        void SubmitMoveServerRpc(string fromSquare, string toSquare)
        {
            Debug.Log($"Server received move: {fromSquare} -> {toSquare}");

            // Update board on server (add your board logic here)

            // Notify all clients
            UpdateBoardClientRpc(fromSquare, toSquare);
        }

        // Send updated move to clients
        [ClientRpc]
        void UpdateBoardClientRpc(string fromSquare, string toSquare)
        {
            Debug.Log($"Client updating move: {fromSquare} -> {toSquare}");

            // Update board visually on client side (add your board UI logic here)
        }
    }
}
