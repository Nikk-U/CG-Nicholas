using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class ProfileImageFixer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image hostProfileImage;
    [SerializeField] private Image clientProfileImage;
    
    [Header("Settings")]
    [SerializeField] private float refreshInterval = 0.5f; // Seconds between forced refreshes
    [SerializeField] private bool debugMode = true;
    
    // Determines if this instance is host or client
    private bool isHost = false;
    
    // Cache tracked skins
    private string lastHostSkin = "";
    private string lastClientSkin = "";
    
    private void Awake()
    {
        // Determine if host or client
        #if UNITY_EDITOR
        try {
            isHost = !ParrelSync.ClonesManager.IsClone();
        } catch {
            isHost = true; // Default to host if ParrelSync isn't available
        }
        #endif
        
        if (debugMode) Debug.Log($"[ProfileFixer] Started as {(isHost ? "Host" : "Client")}");
        
        // Start refresh coroutine
        StartCoroutine(RefreshProfileImages());
    }
    
    
    private IEnumerator RefreshProfileImages()
    {
        WaitForSeconds wait = new WaitForSeconds(refreshInterval);
        
        while (true)
        {
            // Check for changed host skin
            string currentHostSkin = PlayerPrefs.GetString("Host_CurrentSkin", "default");
            if (currentHostSkin != lastHostSkin && currentHostSkin != "default")
            {
                ApplySkinToImage(currentHostSkin, true);
                lastHostSkin = currentHostSkin;
            }
            
            // Check for changed client skin
            string currentClientSkin = PlayerPrefs.GetString("Client_CurrentSkin", "default");
            if (currentClientSkin != lastClientSkin && currentClientSkin != "default")
            {
                ApplySkinToImage(currentClientSkin, false);
                lastClientSkin = currentClientSkin;
            }
            
            yield return wait;
        }
    }
    
    
    public void ApplySkinToImage(string skinId, bool isHostSkin)
    {
        // Choose the correct target image
        Image targetImage = isHostSkin ? hostProfileImage : clientProfileImage;
        
        if (targetImage == null)
        {
            if (debugMode) Debug.LogError($"[ProfileFixer] Target image is null for {(isHostSkin ? "host" : "client")}");
            return;
        }
        
        // Get the skin file path
        string skinPath = Path.Combine(Application.persistentDataPath, skinId + ".png");
        
        // Check if file exists
        if (!File.Exists(skinPath))
        {
            if (debugMode) Debug.LogWarning($"[ProfileFixer] Skin file not found: {skinPath}");
            return;
        }
        
        try
        {
            // Load and apply the texture
            Texture2D texture = new Texture2D(2, 2);
            byte[] fileData = File.ReadAllBytes(skinPath);
            
            if (texture.LoadImage(fileData))
            {
                targetImage.sprite = Sprite.Create(
                    texture, 
                    new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * 0.5f
                );
                targetImage.preserveAspect = true;
                
                if (debugMode) Debug.Log($"[ProfileFixer] Applied {skinId} to {(isHostSkin ? "host" : "client")} profile");
            }
            else
            {
                if (debugMode) Debug.LogError($"[ProfileFixer] Failed to load texture for {skinId}");
            }
        }
        catch (System.Exception ex)
        {
            if (debugMode) Debug.LogError($"[ProfileFixer] Error applying skin: {ex.Message}");
        }
    }
    
    
    public void ForceRefreshAllProfiles()
    {
        string hostSkin = PlayerPrefs.GetString("Host_CurrentSkin", "default");
        string clientSkin = PlayerPrefs.GetString("Client_CurrentSkin", "default");
        
        if (hostSkin != "default")
        {
            ApplySkinToImage(hostSkin, true);
            lastHostSkin = hostSkin;
        }
        
        if (clientSkin != "default")
        {
            ApplySkinToImage(clientSkin, false);
            lastClientSkin = clientSkin;
        }
        
        if (debugMode) Debug.Log($"[ProfileFixer] Forced refresh of all profiles. Host: {hostSkin}, Client: {clientSkin}");
    }
}