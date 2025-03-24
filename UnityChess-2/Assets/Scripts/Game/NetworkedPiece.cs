using Unity.Netcode;
using UnityEngine;

public class NetworkedPiece : NetworkBehaviour
{
    public void RequestMove(Vector3 targetPosition)
    {
        // Only the owning client should request a move.
        if (!IsOwner) return;

        // Call the server to request the move.
        RequestMoveServerRpc(targetPosition);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestMoveServerRpc(Vector3 targetPosition, ServerRpcParams rpcParams = default)
    {
        // Retrieve the starting position.
        Vector3 startPosition = transform.position;

        // *** Insert your move validation logic here ***
        // For example, you might call:
        // if (!GameManager.Instance.TryExecuteMove(...)) { return; }
        // where you pass in the piece's current square (derived from startPosition) and the target square.
        //
        // For this example, we assume the move is valid.

        // Optionally, if the move involves special behavior (e.g., promotion, castling) you can handle that here.

        // Now, update the authoritative game state on the server and notify all clients.
        UpdatePositionClientRpc(targetPosition);
    }

    [ClientRpc]
    private void UpdatePositionClientRpc(Vector3 targetPosition, ClientRpcParams rpcParams = default)
    {
        transform.position = targetPosition;
    }
}