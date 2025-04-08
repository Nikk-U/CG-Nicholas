using UnityEngine;
using UnityEngine.UI;

public class StoreButtonController : MonoBehaviour
{
    [SerializeField] private Button storeButton;
    [SerializeField] private GameObject storeButtonVisual; // Optional visual indicator for the button
    
    private void Start()
    {
        if (storeButton != null)
        {
            // Add listener to the button
            storeButton.onClick.AddListener(OpenStore);
        }
    }
    
    private void OpenStore()
    {
        // Find the DLC Manager and open the store
        FirebaseDLCManager dlcManager = FirebaseDLCManager.Instance;
        if (dlcManager != null)
        {
            dlcManager.OpenStore();
        }
        else
        {
            Debug.LogError("FirebaseDLCManager not found in scene!");
        }
    }
    
    // Optional method to toggle button visibility
    public void SetStoreButtonVisible(bool isVisible)
    {
        if (storeButtonVisual != null)
        {
            storeButtonVisual.SetActive(isVisible);
        }
    }
}