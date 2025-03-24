using Unity.Netcode;
using UnityEngine;

public class NetworkInitialize : MonoBehaviour
{
    private void Start()
    {
        // Check if the NetworkManager component is attached
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager is not found. Please add a NetworkManager component to a GameObject in the scene.");
        }
    }
}