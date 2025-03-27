using Unity.Netcode;
using UnityEngine;

public class RpcTest : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer && IsOwner)
        {
            // Send RPC to server
            SendToServerRpc(0, NetworkObjectId);
        }
    }

    [ClientRpc]
    void SendToClientsClientRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"Client Received the RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        if (IsOwner)
        {
            SendToServerRpc(value + 1, sourceNetworkObjectId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void SendToServerRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"Server Received the RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        SendToClientsClientRpc(value, sourceNetworkObjectId);
    }
}