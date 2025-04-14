using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class MultiplayerProfileSync : MonoBehaviour
{
    // References to profile images in game UI
    [SerializeField] private Image hostProfileImage;
    [SerializeField] private Image clientProfileImage;
    
    // For ParrelSync communication
    private static string syncedSkinKey = "ParrelSync_CurrentSkin";
    private static string syncedPlayerIdKey = "ParrelSync_CurrentSkin_PlayerId";
    
    // Determines if this is host or client
    private bool isHost = false;
    private string myPlayerId;
    
    // Last known synced skins to prevent redundant updates
    private string lastHostSkin = "";
    private string lastClientSkin = "";
    
    // Debug options
    [SerializeField] private bool debugMode = false;
    
    // Flag to prevent conflicts with FirebaseDLCManager
    private bool shouldProcessUpdates = false;
    
    private void Awake()
    {
        // Determine if this is the host (for ParrelSync)
        #if UNITY_EDITOR
        try {
            isHost = !ParrelSync.ClonesManager.IsClone();
        } catch {
            isHost = true; // Default to host if ParrelSync isn't available
        }
        #endif
        
        myPlayerId = isHost ? "Host" : "Client";
        
        if (debugMode)
        {
            Debug.Log("MultiplayerProfileSync initialized as: " + myPlayerId);
        }
    }
    
    private void OnEnable()
    {
        // Initially skip updates to avoid conflicts with FirebaseDLCManager
        // Wait 1 second before enabling updates to make sure FirebaseDLCManager is fully initialized
        StartCoroutine(EnableUpdatesAfterDelay(1.0f));
        
        // Subscribe to profile update events
        FirebaseDLCManager.OnProfileUpdated += OnProfileUpdated;
        
        // Start checking for profile updates
        StartCoroutine(CheckForProfileUpdates());
    }
    
    private void OnDisable()
    {
        // Unsubscribe from events
        FirebaseDLCManager.OnProfileUpdated -= OnProfileUpdated;
    }
    
    private void Start()
    {
        // Load initial profiles
        LoadInitialProfiles();
    }
    
    private IEnumerator EnableUpdatesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        shouldProcessUpdates = true;
        if (debugMode)
        {
            Debug.Log($"MultiplayerProfileSync updates enabled after {delay}s delay");
        }
    }
    
    private void LoadInitialProfiles()
    {
        // Load saved profile images based on PlayerPrefs
        string hostSkinId = PlayerPrefs.GetString("Host_CurrentSkin", "default");
        string clientSkinId = PlayerPrefs.GetString("Client_CurrentSkin", "default");
        
        // If valid skins exist in persistent data, load them
        if (hostSkinId != "default")
        {
            UpdateProfileImage(hostProfileImage, hostSkinId);
            lastHostSkin = hostSkinId;
        }
        
        if (clientSkinId != "default")
        {
            UpdateProfileImage(clientProfileImage, clientSkinId);
            lastClientSkin = clientSkinId;
        }
        
        if (debugMode)
        {
            Debug.Log("Initial profiles loaded - Host: " + hostSkinId + ", Client: " + clientSkinId);
        }
    }
    
    private void OnProfileUpdated(string playerId, string skinId)
    {
        // Skip processing if updates are disabled
        if (!shouldProcessUpdates) return;
        
        if (debugMode)
        {
            Debug.Log($"Profile update event received: {playerId} changed to {skinId}");
        }
        
      
        if (playerId == "Host" && !isHost) // Only update host profile if we're the client
        {
            UpdateProfileImage(hostProfileImage, skinId);
            lastHostSkin = skinId;
            
            // Save to PlayerPrefs for persistence
            PlayerPrefs.SetString("Host_CurrentSkin", skinId);
            PlayerPrefs.Save();
        }
        else if (playerId == "Client" && isHost) // Only update client profile if we're the host
        {
            UpdateProfileImage(clientProfileImage, skinId);
            lastClientSkin = skinId;
            
            // Save to PlayerPrefs for persistence
            PlayerPrefs.SetString("Client_CurrentSkin", skinId);
            PlayerPrefs.Save();
        }
    }
    
    private void UpdateProfileImage(Image profileImage, string skinId)
    {
        if (profileImage == null)
        {
            Debug.LogError("Profile image is null");
            return;
        }
        
        // Load skin image from persistent data path
        string skinPath = Path.Combine(Application.persistentDataPath, skinId + ".png");
        if (File.Exists(skinPath))
        {
            try
            {
                Texture2D texture = new Texture2D(2, 2);
                byte[] fileData = File.ReadAllBytes(skinPath);
                if (texture.LoadImage(fileData))
                {
                    profileImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
                    profileImage.preserveAspect = true;
                    
                    if (debugMode)
                    {
                        Debug.Log($"Profile image updated successfully: {skinId}");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to load image data for skin: {skinId}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error updating profile image: {ex.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"Profile image file not found: {skinPath}");
        }
    }
    
    private IEnumerator CheckForProfileUpdates()
    {
        WaitForSeconds checkInterval = new WaitForSeconds(0.5f);
        
        while (true)
        {
            // Skip processing if updates are disabled
            if (shouldProcessUpdates)
            {
                // Check for updates from other instances via PlayerPrefs
                string syncedSkin = PlayerPrefs.GetString(syncedSkinKey, "");
                string syncedPlayerId = PlayerPrefs.GetString(syncedPlayerIdKey, "");
                
                // Only process if we have valid data
                if (!string.IsNullOrEmpty(syncedSkin) && !string.IsNullOrEmpty(syncedPlayerId))
                {
                    if (syncedPlayerId == "Host" && syncedSkin != lastHostSkin && !isHost)
                    {
                        // Host skin updated and we're the client
                        if (debugMode)
                        {
                            Debug.Log($"Detected host skin update via PlayerPrefs: {syncedSkin}");
                        }
                        
                        UpdateProfileImage(hostProfileImage, syncedSkin);
                        lastHostSkin = syncedSkin;
                        
                        // Save to PlayerPrefs for persistence
                        PlayerPrefs.SetString("Host_CurrentSkin", syncedSkin);
                        PlayerPrefs.Save();
                    }
                    else if (syncedPlayerId == "Client" && syncedSkin != lastClientSkin && isHost)
                    {
                        // Client skin updated and we're the host
                        if (debugMode)
                        {
                            Debug.Log($"Detected client skin update via PlayerPrefs: {syncedSkin}");
                        }
                        
                        UpdateProfileImage(clientProfileImage, syncedSkin);
                        lastClientSkin = syncedSkin;
                        
                        // Save to PlayerPrefs for persistence
                        PlayerPrefs.SetString("Client_CurrentSkin", syncedSkin);
                        PlayerPrefs.Save();
                    }
                }
            }
            
            yield return checkInterval;
        }
    }
}