using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Storage;
using Firebase.Extensions;
using System.IO;
using System.Threading.Tasks;

public class FirebaseDLCManager : MonoBehaviour
{
    // Singleton pattern setup
    public static FirebaseDLCManager Instance { get; private set; }

    // Firebase Storage references
    private FirebaseStorage storage;
    private StorageReference storageReference;

    // Player data
    [SerializeField] private int playerCredits = 1000; // Starting credits
    private Dictionary<string, bool> ownedSkins = new Dictionary<string, bool>();
    private string currentProfileSkin = "default";

    // Debug options
    [SerializeField] private bool debugMode = true; // Set to true by default for debugging
    [SerializeField] private TextMeshProUGUI debugText; // Optional debug text display in UI
    private List<string> debugMessages = new List<string>();
    private int maxDebugMessages = 10;

    // Skin data structure
    [System.Serializable]
    public class SkinItem
    {
        public string skinId;
        public string skinName;
        public int price;
        public Image previewImageDisplay; // UI Image component to display the preview image
        public TextMeshProUGUI nameText; // Display name text
        public TextMeshProUGUI priceText; // Display price text
        public GameObject ownedIndicator; // Visual indicator for when skin is owned
        public GameObject equippedIndicator; // Visual indicator for when skin is equipped
        public Button purchaseButton; // Single button for both purchase and equip
    }

    // List of available skins
    public List<SkinItem> availableSkins = new List<SkinItem>();

    // UI References
    [SerializeField] private TextMeshProUGUI creditsText;
    [SerializeField] private Image hostProfileImage;
    [SerializeField] private Image clientProfileImage;
    [SerializeField] private GameObject storePanel;

    // Network events
    public delegate void ProfileUpdatedEvent(string playerId, string skinId);
    public static event ProfileUpdatedEvent OnProfileUpdated;

    // Host/Client identification
    private bool isHost = false;
    private string playerId;

    // Current skins for host and client
    private string hostCurrentSkin = "default";
    private string clientCurrentSkin = "default";

    // Reference to ChessRelay for network synchronization
    private ChessRelay chessRelay;

    // Flag to prevent recursive sync
    private bool isHandlingRemoteUpdate = false;

    private void Awake()
    {
        // Singleton implementation
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Determine if host or client
        #if UNITY_EDITOR
        try
        {
            isHost = !ParrelSync.ClonesManager.IsClone();
        }
        catch
        {
            isHost = true; // Default to host if ParrelSync isn't available
        }
        #endif
        playerId = isHost ? "Host" : "Client";

        AddDebugMessage("Initialized as: " + playerId);

        // Initialize Firebase
        InitializeFirebase();

        // Load player saved data
        LoadPlayerData();
    }

    private void Start()
    {
        // Update UI
        UpdateCreditsDisplay();

        // Hide store panel initially
        if (storePanel != null)
        {
            storePanel.SetActive(false);
        }

        // Get ChessRelay reference
        chessRelay = FindObjectOfType<ChessRelay>();
        if (chessRelay == null)
        {
            AddDebugMessage("WARNING: ChessRelay not found, multiplayer sync will not work!");
        }
        else
        {
            AddDebugMessage("ChessRelay found, multiplayer sync enabled");
        }

        // Fetch available skins from Firebase
        StartCoroutine(FetchAvailableSkins());

        // Load initial profile images
        LoadInitialProfiles();
        
        // Force correct profile images
        ForceCorrectProfileImages();
    }

    private void LoadInitialProfiles()
    {
        // Load saved profile settings
        string hostSkinId = PlayerPrefs.GetString("Host_CurrentSkin", "default");
        string clientSkinId = PlayerPrefs.GetString("Client_CurrentSkin", "default");

        // Track current skins
        hostCurrentSkin = hostSkinId;
        clientCurrentSkin = clientSkinId;

        // Our local profile is based on whether we're host or client
        currentProfileSkin = isHost ? hostSkinId : clientSkinId;

        AddDebugMessage($"Initial profiles - Host: {hostSkinId}, Client: {clientSkinId}");

        // Update profile images
        UpdateProfileImages();
    }

    private void UpdateProfileImages()
    {
        // Update host profile
        if (hostProfileImage != null && !string.IsNullOrEmpty(hostCurrentSkin) && hostCurrentSkin != "default")
        {
            string hostSkinPath = Path.Combine(Application.persistentDataPath, hostCurrentSkin + ".png");
            if (File.Exists(hostSkinPath))
            {
                ApplySkinToImage(hostSkinPath, hostProfileImage);
            }
            else
            {
                AddDebugMessage($"Host skin file not found: {hostSkinPath}");
            }
        }

        // Update client profile
        if (clientProfileImage != null && !string.IsNullOrEmpty(clientCurrentSkin) && clientCurrentSkin != "default")
        {
            string clientSkinPath = Path.Combine(Application.persistentDataPath, clientCurrentSkin + ".png");
            if (File.Exists(clientSkinPath))
            {
                ApplySkinToImage(clientSkinPath, clientProfileImage);
            }
            else
            {
                AddDebugMessage($"Client skin file not found: {clientSkinPath}");
            }
        }
    }

    private void InitializeFirebase()
    {
        try
        {
            // Initialize Firebase Storage
            storage = FirebaseStorage.DefaultInstance;
            storageReference = storage.GetReferenceFromUrl("gs://nicholasdlc.firebasestorage.app");
            AddDebugMessage("Firebase Storage initialized successfully");
        }
        catch (Exception ex)
        {
            AddDebugMessage("Error initializing Firebase: " + ex.Message);
        }
    }

    private IEnumerator FetchAvailableSkins()
    {
        AddDebugMessage("Starting to fetch available skins...");

        // Setup predefined skin data
        string[] skinIds = new string[] { "Bishop", "BishopIce", "King", "Knight" };
        string[] skinNames = new string[] { "Red Bishop", "BishopIce", "King", "Knight" };
        int[] skinPrices = new int[] { 20, 20, 20, 20 };

        for (int i = 0; i < skinIds.Length && i < availableSkins.Count; i++)
        {
            string skinId = skinIds[i];

            // Set initial name and price texts
            if (availableSkins[i].nameText != null)
            {
                availableSkins[i].nameText.text = skinNames[i];
            }

            if (availableSkins[i].priceText != null)
            {
                availableSkins[i].priceText.text = skinPrices[i] + " Credits";
            }

            // Check if we already downloaded this skin's preview
            string previewPath = Path.Combine(Application.persistentDataPath, skinId + "_preview.png");
            bool previewDownloaded = File.Exists(previewPath);

            if (previewDownloaded)
            {
                AddDebugMessage("Preview for " + skinId + " already cached");

                // Load preview from cached file
                LoadPreviewFromCache(skinId, availableSkins[i].previewImageDisplay);
            }
            else
            {
                AddDebugMessage("Downloading preview for " + skinId + " from Firebase");

                // Download preview from Firebase
                yield return StartCoroutine(DownloadSkinPreview(skinId, i));
            }

            // Check if this skin is owned
            bool isOwned = ownedSkins.ContainsKey(skinId) && ownedSkins[skinId];
            bool isEquipped = (isHost && hostCurrentSkin == skinId) || (!isHost && clientCurrentSkin == skinId);

            // Update the UI based on ownership and equipped status
            UpdateSkinUI(i, isOwned, isEquipped);

            // Give a small delay between each skin to avoid overloading Firebase
            yield return new WaitForSeconds(0.2f);
        }

        AddDebugMessage("All skin previews fetched successfully");
    }

    private void LoadPreviewFromCache(string skinId, Image targetImage)
    {
        string previewPath = Path.Combine(Application.persistentDataPath, skinId + "_preview.png");
        if (File.Exists(previewPath) && targetImage != null)
        {
            try
            {
                Texture2D texture = new Texture2D(2, 2);
                byte[] fileData = File.ReadAllBytes(previewPath);
                if (texture.LoadImage(fileData))
                {
                    targetImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        Vector2.one * 0.5f);
                    targetImage.preserveAspect = true;
                    AddDebugMessage("Loaded cached preview for " + skinId);
                }
                else
                {
                    AddDebugMessage("Failed to load cached preview texture for " + skinId);
                }
            }
            catch (Exception ex)
            {
                AddDebugMessage("Error loading cached preview: " + ex.Message);
            }
        }
        else
        {
            AddDebugMessage("Cached preview not found for " + skinId);
        }
    }

    private IEnumerator DownloadSkinPreview(string skinId, int index)
    {
        AddDebugMessage("Starting download of preview for: " + skinId);

        if (index >= availableSkins.Count)
        {
            AddDebugMessage("Error: Index out of range for skin: " + skinId);
            yield break;
        }

        // Start spinner or loading indicator
        if (availableSkins[index].previewImageDisplay != null)
        {
            // Optional: Show loading animation 
            availableSkins[index].previewImageDisplay.color = new Color(0.5f, 0.5f, 0.5f); // Dim the image
        }

        // Get reference to the skin in Firebase Storage
        StorageReference skinRef = storageReference.Child("skins").Child(skinId + ".png");

        // Local file path for the downloaded preview
        string previewPath = Path.Combine(Application.persistentDataPath, skinId + "_preview.png");

        AddDebugMessage("Firebase path: " + skinRef.Path);

        // Download to local file
        Task downloadTask = null;

        try
        {
            downloadTask = skinRef.GetFileAsync(previewPath);
        }
        catch (Exception ex)
        {
            AddDebugMessage("Failed to start download: " + ex.Message);
            yield break;
        }

        // Wait for download to complete
        while (!downloadTask.IsCompleted)
        {
            // You could update a progress bar here
            yield return null;
        }

        if (downloadTask.IsFaulted)
        {
            AddDebugMessage("Download failed for " + skinId + ": " + downloadTask.Exception.Message);

            // Reset image color
            if (availableSkins[index].previewImageDisplay != null)
            {
                availableSkins[index].previewImageDisplay.color = Color.white;
            }
        }
        else if (downloadTask.IsCompleted)
        {
            AddDebugMessage("Download successful for " + skinId);

            // Reset image color
            if (availableSkins[index].previewImageDisplay != null)
            {
                availableSkins[index].previewImageDisplay.color = Color.white;
            }

            // Load the preview into the Image component
            try
            {
                if (availableSkins[index].previewImageDisplay != null)
                {
                    Texture2D texture = new Texture2D(2, 2);
                    byte[] fileData = File.ReadAllBytes(previewPath);
                    if (texture.LoadImage(fileData))
                    {
                        availableSkins[index].previewImageDisplay.sprite = Sprite.Create(texture,
                            new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
                        availableSkins[index].previewImageDisplay.preserveAspect = true;
                        AddDebugMessage("Preview image loaded for " + skinId);
                    }
                    else
                    {
                        AddDebugMessage("Failed to parse image data for " + skinId);
                    }
                }
                else
                {
                    AddDebugMessage("Preview image display is null for " + skinId);
                }
            }
            catch (Exception ex)
            {
                AddDebugMessage("Error applying preview: " + ex.Message);
            }
        }
    }

    private IEnumerator DownloadFullSkin(string skinId)
    {
        AddDebugMessage("Downloading full skin: " + skinId);

        // Get reference to the full skin in Firebase Storage
        StorageReference skinRef = storageReference.Child("skins").Child(skinId + ".png");

        // Local file path for the downloaded skin
        string localPath = Path.Combine(Application.persistentDataPath, skinId + ".png");

        // Download to local file
        Task downloadTask = skinRef.GetFileAsync(localPath);

        // Wait for download to complete
        while (!downloadTask.IsCompleted)
        {
            yield return null;
        }

        if (downloadTask.IsFaulted)
        {
            AddDebugMessage("Failed to download skin: " + downloadTask.Exception.Message);
        }
        else if (downloadTask.IsCompleted)
        {
            AddDebugMessage("Skin downloaded successfully: " + skinId);

            // Mark as owned
            ownedSkins[skinId] = true;

            // Save player data
            SavePlayerData();

            // Set as current skin for this player (host or client)
            if (isHost)
            {
                PlayerPrefs.SetString("Host_CurrentSkin", skinId);
                AddDebugMessage($"HOST: Equipping skin {skinId}");
            }
            else
            {
                PlayerPrefs.SetString("Client_CurrentSkin", skinId);
                AddDebugMessage($"CLIENT: Equipping skin {skinId}");
            }
            
            // Save the current profile skin
            currentProfileSkin = skinId;
            PlayerPrefs.Save();
            
            // Force correct images
            ForceCorrectProfileImages();
            
            // Notify network about profile update
            NotifyProfileUpdate(playerId, skinId);

            // If in a multiplayer game, notify via ChessRelay (Netcode)
            if (chessRelay != null && Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsConnectedClient)
            {
                AddDebugMessage($"NETWORK: Sending skin update for {skinId}");
                chessRelay.NotifySkinPurchasedServerRpc(skinId);
            }

            // Update UI indicators for all skins
            RefreshStoreUI();
        }
    }

    private void UpdateSkinUI(int index, bool isOwned, bool isEquipped)
    {
        if (index < availableSkins.Count)
        {
            SkinItem skin = availableSkins[index];

            // Update owned indicator
            if (skin.ownedIndicator != null)
            {
                skin.ownedIndicator.SetActive(isOwned);
            }

            // Update equipped indicator
            if (skin.equippedIndicator != null)
            {
                skin.equippedIndicator.SetActive(isEquipped);
            }

            // Update purchase button
            if (isOwned)
            {
                // Change button function to "Equip" if owned
                skin.purchaseButton.onClick.RemoveAllListeners();
                skin.purchaseButton.onClick.AddListener(() => { EquipSkin(skin.skinId); });

                // Update visual state (optional: you can change button text or color)
                ColorBlock colors = skin.purchaseButton.colors;
                colors.normalColor =
                    isEquipped ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.2f, 0.6f, 0.2f); // Green for owned
                skin.purchaseButton.colors = colors;
            }
            else
            {
                // Set for purchase
                skin.purchaseButton.onClick.RemoveAllListeners();
                skin.purchaseButton.onClick.AddListener(() => { PurchaseSkin(skin.skinId); });

                // Reset button color
                ColorBlock colors = skin.purchaseButton.colors;
                colors.normalColor = Color.white;
                skin.purchaseButton.colors = colors;
            }
        }
    }

    // Method to purchase skin directly without confirmation
    public void PurchaseSkin(string skinId)
    {
        // Find the skin
        SkinItem skinToPurchase = availableSkins.Find(s => s.skinId == skinId);

        if (skinToPurchase == null)
        {
            AddDebugMessage("Skin not found: " + skinId);
            return;
        }

        // Check if already owned
        if (ownedSkins.ContainsKey(skinId) && ownedSkins[skinId])
        {
            AddDebugMessage("Skin already owned: " + skinId);
            EquipSkin(skinId);
            return;
        }

        // Check if player has enough credits
        if (playerCredits < skinToPurchase.price)
        {
            AddDebugMessage("Not enough credits");
            // Flash credits text red
            StartCoroutine(FlashCreditsText());
            return;
        }

        // Deduct credits
        playerCredits -= skinToPurchase.price;
        UpdateCreditsDisplay();

        AddDebugMessage("Purchase initiated for " + skinId);

        // Download the full skin (will automatically equip when done)
        StartCoroutine(DownloadFullSkin(skinId));
    }

    // Modified EquipSkin method that uses the force override approach
    public void EquipSkin(string skinId)
    {
        // Check if skin is owned
        if (!ownedSkins.ContainsKey(skinId) || !ownedSkins[skinId])
        {
            AddDebugMessage("Skin not owned: " + skinId);
            return;
        }
        
        // Set current skin for this player (host or client)
        if (isHost)
        {
            PlayerPrefs.SetString("Host_CurrentSkin", skinId);
            AddDebugMessage($"HOST: Equipping skin {skinId}");
        }
        else
        {
            PlayerPrefs.SetString("Client_CurrentSkin", skinId);
            AddDebugMessage($"CLIENT: Equipping skin {skinId}");
        }
        
        // Save the current profile skin
        currentProfileSkin = skinId;
        PlayerPrefs.Save();
        
        // Force correct images
        ForceCorrectProfileImages();
        
        // Notify network about profile update
        NotifyProfileUpdate(playerId, skinId);
        
        // Send network message if connected
        if (chessRelay != null && Unity.Netcode.NetworkManager.Singleton != null && 
            Unity.Netcode.NetworkManager.Singleton.IsConnectedClient)
        {
            AddDebugMessage($"NETWORK: Sending skin update for {skinId}");
            chessRelay.NotifySkinPurchasedServerRpc(skinId);
        }
        
        // Update UI to reflect equipped status
        RefreshStoreUI();
    }

    // Override for handling network skin update
    // This is a complete rewrite that uses a much simpler approach
    public void HandleNetworkSkinUpdate(string skinId, bool fromHost)
    {
        // Prevent recursive updates
        if (isHandlingRemoteUpdate) return;
        isHandlingRemoteUpdate = true;
        
        try
        {
            string updaterPlayerId = fromHost ? "Host" : "Client";
            AddDebugMessage($"NETWORK: Received skin update: {skinId} from {updaterPlayerId}");
            
            // If we're host and update is from client
            if (isHost && !fromHost)
            {
                // Host processing client skin update
                PlayerPrefs.SetString("Client_CurrentSkin", skinId);
                AddDebugMessage($"HOST: Setting client skin to {skinId}");
                
                // Mark as owned
                if (!ownedSkins.ContainsKey(skinId))
                {
                    ownedSkins[skinId] = true;
                    SavePlayerData();
                }
            }
            // If we're client and update is from host
            else if (!isHost && fromHost)
            {
                // Client processing host skin update
                PlayerPrefs.SetString("Host_CurrentSkin", skinId);
                AddDebugMessage($"CLIENT: Setting host skin to {skinId}");
                
                // Mark as owned
                if (!ownedSkins.ContainsKey(skinId))
                {
                    ownedSkins[skinId] = true;
                    SavePlayerData();
                }
            }
            
            // After updating preferences, force the correct images to display
            ForceCorrectProfileImages();
        }
        finally
        {
            isHandlingRemoteUpdate = false;
        }
    }

    // Override method to force correct profile images
    public void ForceCorrectProfileImages()
    {
        // Force host and client profiles to match their saved settings
        string hostSkinId = PlayerPrefs.GetString("Host_CurrentSkin", "default");
        string clientSkinId = PlayerPrefs.GetString("Client_CurrentSkin", "default");
        
        AddDebugMessage($"FORCE OVERRIDE: Applying host skin: {hostSkinId}, client skin: {clientSkinId}");
        
        // Update the host profile image
        if (!string.IsNullOrEmpty(hostSkinId) && hostSkinId != "default" && hostProfileImage != null)
        {
            string hostSkinPath = Path.Combine(Application.persistentDataPath, hostSkinId + ".png");
            if (File.Exists(hostSkinPath))
            {
                // Directly apply the host skin to host profile
                ApplySkinToImage(hostSkinPath, hostProfileImage);
                AddDebugMessage($"OVERRIDE: Applied {hostSkinId} to host profile");
            }
        }
        
        // Update the client profile image
        if (!string.IsNullOrEmpty(clientSkinId) && clientSkinId != "default" && clientProfileImage != null)
        {
            string clientSkinPath = Path.Combine(Application.persistentDataPath, clientSkinId + ".png");
            if (File.Exists(clientSkinPath))
            {
                // Directly apply the client skin to client profile
                ApplySkinToImage(clientSkinPath, clientProfileImage);
                AddDebugMessage($"OVERRIDE: Applied {clientSkinId} to client profile");
            }
        }
        
        // Update instance tracking variables
        hostCurrentSkin = hostSkinId;
        clientCurrentSkin = clientSkinId;
        currentProfileSkin = isHost ? hostSkinId : clientSkinId;
        
        // Update UI
        RefreshStoreUI();
    }

    private IEnumerator DownloadNetworkSkin(string skinId, bool fromHost)
    {
        AddDebugMessage($"Downloading network skin: {skinId} from {(fromHost ? "Host" : "Client")}");

        // Get reference to the skin in Firebase Storage
        StorageReference skinRef = storageReference.Child("skins").Child(skinId + ".png");

        // Local file path
        string localPath = Path.Combine(Application.persistentDataPath, skinId + ".png");

        // Download the file
        Task downloadTask = skinRef.GetFileAsync(localPath);

        // Wait for download to complete
        while (!downloadTask.IsCompleted)
        {
            yield return null;
        }

        if (downloadTask.IsFaulted)
        {
            AddDebugMessage($"Failed to download network skin: {downloadTask.Exception.Message}");
        }
        else if (downloadTask.IsCompleted)
        {
            AddDebugMessage($"Network skin downloaded successfully: {skinId}");
            
            // After download completes, force correct profile images
            ForceCorrectProfileImages();
        }
    }
    
    private void ApplySkinToImage(string skinPath, Image targetImage)
    {
        if (targetImage == null)
        {
            AddDebugMessage("Target image is null");
            return;
        }

        try
        {
            Texture2D texture = new Texture2D(2, 2);
            byte[] fileData = File.ReadAllBytes(skinPath);
            if (texture.LoadImage(fileData))
            {
                targetImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * 0.5f);
                targetImage.preserveAspect = true;
                AddDebugMessage("Applied skin to image successfully");
            }
            else
            {
                AddDebugMessage("Failed to load skin texture");
            }
        }
        catch (Exception ex)
        {
            AddDebugMessage($"Error applying skin to image: {ex.Message}");
        }
    }

    private void UpdateOwnProfileImage()
    {
        // The Force Correct Images method is better, so we'll use that instead
        ForceCorrectProfileImages();
    }

    private void UpdateCreditsDisplay()
    {
        if (creditsText != null)
        {
            creditsText.text = "Credits: " + playerCredits.ToString();
        }
    }

    private IEnumerator FlashCreditsText()
    {
        Color originalColor = creditsText.color;
        creditsText.color = Color.red;
        yield return new WaitForSeconds(0.5f);
        creditsText.color = originalColor;
    }

    private void SavePlayerData()
    {
        // Convert owned skins to JSON
        string ownedSkinsJson =
            JsonUtility.ToJson(new SkinsData { ownedSkinsList = new List<string>(ownedSkins.Keys) });

        // Save owned skins
        PlayerPrefs.SetString("OwnedSkins", ownedSkinsJson);

        // Save credits
        PlayerPrefs.SetInt("PlayerCredits", playerCredits);

        // Save current skin
        PlayerPrefs.SetString("CurrentProfileSkin", currentProfileSkin);

        // Save the data
        PlayerPrefs.Save();

        AddDebugMessage("Player data saved");
    }

    private void LoadPlayerData()
    {
        // Load credits
        if (PlayerPrefs.HasKey("PlayerCredits"))
        {
            playerCredits = PlayerPrefs.GetInt("PlayerCredits");
            AddDebugMessage("Loaded credits: " + playerCredits);
        }

        // Load owned skins
        if (PlayerPrefs.HasKey("OwnedSkins"))
        {
            string ownedSkinsJson = PlayerPrefs.GetString("OwnedSkins");
            try
            {
                SkinsData skinsData = JsonUtility.FromJson<SkinsData>(ownedSkinsJson);

                // Reset and refill owned skins dictionary
                ownedSkins.Clear();
                foreach (string skinId in skinsData.ownedSkinsList)
                {
                    ownedSkins[skinId] = true;
                }

                AddDebugMessage("Loaded " + ownedSkins.Count + " owned skins");
            }
            catch (Exception ex)
            {
                AddDebugMessage("Error loading owned skins: " + ex.Message);
                ownedSkins.Clear();
            }
        }

        // Load current skin
        if (PlayerPrefs.HasKey("CurrentProfileSkin"))
        {
            currentProfileSkin = PlayerPrefs.GetString("CurrentProfileSkin");
            AddDebugMessage("Loaded current skin: " + currentProfileSkin);
        }
    }

    // Helper class for JSON serialization
    [Serializable]
    private class SkinsData
    {
        public List<string> ownedSkinsList = new List<string>();
    }

    // Network Methods
    private void NotifyProfileUpdate(string playerId, string skinId)
    {
        // Trigger the event for all listeners
        OnProfileUpdated?.Invoke(playerId, skinId);

        // For ParrelSync, save to PlayerPrefs as communication channel
        PlayerPrefs.SetString("ParrelSync_CurrentSkin", skinId);
        PlayerPrefs.SetString("ParrelSync_CurrentSkin_PlayerId", playerId);
        PlayerPrefs.Save();

        AddDebugMessage("Profile update notification sent: " + playerId + " using " + skinId);
    }

    // Store UI Control Methods
    public void ToggleStorePanel()
    {
        if (storePanel != null)
        {
            storePanel.SetActive(!storePanel.activeSelf);

            // If opening the store, refresh skin data
            if (storePanel.activeSelf)
            {
                UpdateCreditsDisplay();
                RefreshStoreUI();
            }
        }
    }

    public void OpenStore()
    {
        if (storePanel != null)
        {
            storePanel.SetActive(true);
            UpdateCreditsDisplay();
            RefreshStoreUI();
        }
    }

    public void CloseStore()
    {
        if (storePanel != null)
        {
            storePanel.SetActive(false);
        }
    }

    private void RefreshStoreUI()
    {
        // Update all skin items in the store
        for (int i = 0; i < availableSkins.Count; i++)
        {
            string skinId = availableSkins[i].skinId;
            bool isOwned = ownedSkins.ContainsKey(skinId) && ownedSkins[skinId];

            // Equipped status is based on whether we're host or client
            bool isEquipped = false;
            if (isHost)
            {
                isEquipped = skinId == hostCurrentSkin;
            }
            else
            {
                isEquipped = skinId == clientCurrentSkin;
            }

            UpdateSkinUI(i, isOwned, isEquipped);
        }
    }
    
    // Debug methods
    private void AddDebugMessage(string message)
    {
        if (!debugMode) return;

        // Log to console
        Debug.Log("[DLCManager] " + message);

        // Add to debug messages list
        debugMessages.Add(DateTime.Now.ToString("HH:mm:ss") + ": " + message);

        // Cap the list size
        while (debugMessages.Count > maxDebugMessages)
        {
            debugMessages.RemoveAt(0);
        }

        // Update debug text if available
        UpdateDebugText();
    }

    private void UpdateDebugText()
    {
        if (debugText != null)
        {
            string displayText = "DLC Manager Debug Log:\n";
            for (int i = debugMessages.Count - 1; i >= 0; i--)
            {
                displayText += debugMessages[i] + "\n";
            }

            debugText.text = displayText;
        }
    }

    // Public method to enable/disable debug mode
    public void SetDebugMode(bool enabled)
    {
        debugMode = enabled;
        AddDebugMessage("Debug mode " + (enabled ? "enabled" : "disabled"));
    }
    
    // Method to set the debug text component from another script
    public void SetDebugText(TextMeshProUGUI text)
    {
        debugText = text;
        AddDebugMessage("Debug display connected");
        
        // Show current state
        AddDebugMessage($"Running as: {(isHost ? "Host" : "Client")}");
        AddDebugMessage($"Host skin: {hostCurrentSkin}");
        AddDebugMessage($"Client skin: {clientCurrentSkin}");
        
        // Update debug text
        UpdateDebugText();
    }
}