using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using Firebase.Storage;
using Firebase.Extensions;
using UnityChess;

/// <summary>
/// Manages the DLC store functionality including UI display, skin downloads, and purchase tracking.
/// </summary>
public class DLCStoreManager : MonoBehaviour
{
    // Singleton instance
    public static DLCStoreManager Instance { get; private set; }

    [Header("UI Elements")] public GameObject storePanel;
    public Button closeButton;
    public Button[] skinButtons;
    public Text[] skinPriceTexts;
    public Image[] skinPreviewImages;
    public Button openStoreButton; // Button to open the store

    [Header("Skin Configuration")] [SerializeField]
    private string[] skinNames = { "Green", "Blue", "Red", "Pink" };

    [SerializeField] private float[] skinPrices = { 1.99f, 2.99f, 2.99f, 3.99f };
    [SerializeField] private string storagePath = "Chess_Skins";
    [SerializeField] private bool debugMode = true;

    // Dictionary to track purchased skins
    private Dictionary<string, bool> purchasedSkins = new Dictionary<string, bool>();

    // Dictionary to cache downloaded textures
    private Dictionary<string, Texture2D> skinTextures = new Dictionary<string, Texture2D>();

    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        // Initialize purchased skins dictionary
        foreach (string skinName in skinNames)
        {
            // Check if the skin is already purchased (saved in PlayerPrefs)
            bool isPurchased = PlayerPrefs.GetInt("Skin_" + skinName, 0) == 1;
            purchasedSkins[skinName] = isPurchased;

            if (debugMode)
            {
                Debug.Log($"Skin {skinName} is purchased: {isPurchased}");
            }
        }
    }

    private void Start()
    {
        // Set up UI event handlers
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseStore);

        if (openStoreButton != null)
            openStoreButton.onClick.AddListener(OpenStore);

        // Set up skin buttons
        SetupSkinButtons();

        // Close the store panel initially
        if (storePanel != null)
            storePanel.SetActive(false);

        // Apply the selected skin if any
        ApplySelectedSkin();
    }

    /// <summary>
    /// Sets up the skin buttons with prices and click handlers
    /// </summary>
    private void SetupSkinButtons()
    {
        for (int i = 0; i < skinButtons.Length && i < skinNames.Length; i++)
        {
            int index = i; // Needed for correct lambda capture

            // Set button text and price
            Text buttonText = skinButtons[i].GetComponentInChildren<Text>();
            if (buttonText != null)
                buttonText.text = skinNames[i];

            if (skinPriceTexts.Length > i && skinPriceTexts[i] != null)
                skinPriceTexts[i].text = $"${skinPrices[i]:F2}";

            // Setup button click handler
            skinButtons[i].onClick.AddListener(() => PurchaseSkin(index));

            // Update button visual based on purchase status
            UpdateSkinButtonVisual(i);

            // Load preview image for this skin from Firebase
            LoadSkinPreview(skinNames[i], i);
        }
    }

    /// <summary>
    /// Updates a skin button's visual based on whether it's purchased
    /// </summary>
    private void UpdateSkinButtonVisual(int skinIndex)
    {
        if (skinIndex >= 0 && skinIndex < skinButtons.Length && skinIndex < skinNames.Length)
        {
            bool isPurchased = purchasedSkins[skinNames[skinIndex]];

            // Update button color or text
            ColorBlock colors = skinButtons[skinIndex].colors;
            if (isPurchased)
            {
                // Make the button appear purchased/selected
                colors.normalColor = new Color(0.7f, 1.0f, 0.7f);
                Text priceText = skinPriceTexts[skinIndex];
                if (priceText != null)
                    priceText.text = "OWNED";
            }
            else
            {
                // Default button color
                colors.normalColor = Color.white;
            }

            skinButtons[skinIndex].colors = colors;
        }
    }

    /// <summary>
    /// Opens the DLC store UI
    /// </summary>
    public void OpenStore()
    {
        if (storePanel != null)
            storePanel.SetActive(true);
    }

    /// <summary>
    /// Closes the DLC store UI
    /// </summary>
    public void CloseStore()
    {
        if (storePanel != null)
            storePanel.SetActive(false);
    }

    /// <summary>
    /// Handles the purchasing process for a skin
    /// </summary>
    public void PurchaseSkin(int skinIndex)
    {
        if (skinIndex < 0 || skinIndex >= skinNames.Length)
            return;

        string skinName = skinNames[skinIndex];

        // Check if already purchased
        if (purchasedSkins[skinName])
        {
            // If already purchased, apply the skin
            ApplySkin(skinName);
            Debug.Log($"Applying already purchased skin: {skinName}");
            return;
        }

        // In a real app, you would handle payment processing here
        Debug.Log($"Processing purchase for skin: {skinName} at price: ${skinPrices[skinIndex]:F2}");

        // Simulate a successful purchase
        CompletePurchase(skinName, skinIndex);
    }

    /// <summary>
    /// Completes a successful purchase and synchronizes it across the network
    /// </summary>
    private void CompletePurchase(string skinName, int skinIndex)
    {
        // Mark as purchased
        purchasedSkins[skinName] = true;

        // Save purchase in PlayerPrefs
        PlayerPrefs.SetInt("Skin_" + skinName, 1);
        PlayerPrefs.Save();

        // Update button visual
        UpdateSkinButtonVisual(skinIndex);

        // Download and apply the skin
        DownloadAndApplySkin(skinName);

        Debug.Log($"Successfully purchased skin: {skinName}");

        // Notify the network about the skin purchase
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsConnectedClient &&
            ChessRelay.Instance != null)
        {
            // Notify about the skin purchase via the ChessRelay
            ChessRelay.Instance.NotifySkinPurchasedServerRpc(skinName);
        }
    }

    /// <summary>
    /// Marks a skin as purchased (called from network notifications)
    /// </summary>
    /// <summary>
    /// Marks a skin as purchased (called from network notifications)
    /// </summary>
    public void MarkSkinAsPurchased(string skinName)
    {
        if (!purchasedSkins.ContainsKey(skinName))
            return;

        // Set as purchased
        purchasedSkins[skinName] = true;
        PlayerPrefs.SetInt("Skin_" + skinName, 1);
        PlayerPrefs.Save();

        // Update UI if needed
        for (int i = 0; i < skinNames.Length; i++)
        {
            if (skinNames[i] == skinName)
            {
                UpdateSkinButtonVisual(i);
                break;
            }
        }

        Debug.Log($"Marked skin as purchased: {skinName}");
    }

    /// <summary>
    /// Loads a skin preview image from Firebase Storage
    /// </summary>
    private void LoadSkinPreview(string skinName, int buttonIndex)
    {
        if (buttonIndex >= skinPreviewImages.Length || skinPreviewImages[buttonIndex] == null)
            return;

        // First try to load from local cache
        Texture2D cachedTexture = LoadTextureFromFile(skinName + "_preview");
        if (cachedTexture != null)
        {
            // Use cached preview
            skinPreviewImages[buttonIndex].sprite = Sprite.Create(
                cachedTexture,
                new Rect(0, 0, cachedTexture.width, cachedTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            Debug.Log($"Using cached preview for skin: {skinName}");
            return;
        }

        string previewPath = $"{skinName}_preview.png";

        // Create a reference to the file
        FirebaseStorage storage = FirebaseStorage.DefaultInstance;
        StorageReference mainRef = storage.GetReference(storagePath);
        StorageReference storageRef = mainRef.Child(previewPath);

        if (debugMode)
            Debug.Log($"Loading preview from: {storagePath}/{previewPath}");

        // Download the preview image
        const long maxSize = 1024 * 1024; // 1MB max size
        storageRef.GetBytesAsync(maxSize).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to download preview image for skin: {skinName}. Error: {task.Exception}");

                // Use a default colored texture as fallback
                Texture2D defaultTexture = CreateColorTexture(GetColorForSkin(skinName), 128, 128);

                skinPreviewImages[buttonIndex].sprite = Sprite.Create(
                    defaultTexture,
                    new Rect(0, 0, defaultTexture.width, defaultTexture.height),
                    new Vector2(0.5f, 0.5f)
                );

                return;
            }

            // Convert bytes to texture
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(task.Result);

            // Cache the preview texture
            SaveTextureToFile(texture, skinName + "_preview");

            // Apply to preview image
            skinPreviewImages[buttonIndex].sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );

            Debug.Log($"Successfully loaded preview for skin: {skinName}");
        });
    }

    /// <summary>
    /// Loads a skin preview image from Firebase Storage with a callback
    /// </summary>
    public void LoadSkinPreview(string skinName, System.Action<Sprite> callback)
    {
        // First try to load from local cache
        Texture2D cachedTexture = LoadTextureFromFile(skinName + "_preview");
        if (cachedTexture != null)
        {
            // Use cached preview
            Sprite cachedSprite = Sprite.Create(
                cachedTexture,
                new Rect(0, 0, cachedTexture.width, cachedTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            Debug.Log($"Using cached preview for skin: {skinName}");
            callback?.Invoke(cachedSprite);
            return;
        }

        string previewPath = $"{skinName}_preview.png";

        // Create a reference to the file
        FirebaseStorage storage = FirebaseStorage.DefaultInstance;
        StorageReference mainRef = storage.GetReference(storagePath);
        StorageReference storageRef = mainRef.Child(previewPath);

        if (debugMode)
            Debug.Log($"Loading preview for callback from: {storagePath}/{previewPath}");

        // Download the preview image
        const long maxSize = 1024 * 1024; // 1MB max size
        storageRef.GetBytesAsync(maxSize).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to download preview image for skin: {skinName}. Error: {task.Exception}");

                // Create a default colored texture as fallback
                Texture2D defaultTexture = CreateColorTexture(GetColorForSkin(skinName), 128, 128);

                Sprite fallbackSprite = Sprite.Create(
                    defaultTexture,
                    new Rect(0, 0, defaultTexture.width, defaultTexture.height),
                    new Vector2(0.5f, 0.5f)
                );

                callback?.Invoke(fallbackSprite);
                return;
            }

            // Convert bytes to texture
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(task.Result);

            // Cache the preview texture
            SaveTextureToFile(texture, skinName + "_preview");

            // Create sprite from texture
            Sprite downloadedSprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );

            Debug.Log($"Successfully loaded preview for skin: {skinName}");

            // Invoke the callback with the loaded sprite
            callback?.Invoke(downloadedSprite);
        });
    }

    /// <summary>
    /// Shows a purchase confirmation dialog
    /// </summary>
    public void ShowPurchaseConfirmation(string skinName, float price, System.Action onConfirm)
    {
        // In a real implementation, this would show a UI dialog asking for confirmation
        Debug.Log($"Would you like to purchase {skinName} for ${price:F2}?");

        // For demonstration, we'll just proceed with the purchase
        onConfirm?.Invoke();
    }

    /// <summary>
    /// Downloads a skin from Firebase Storage and applies it
    /// </summary>
    private void DownloadAndApplySkin(string skinName)
    {
        if (debugMode)
            Debug.Log($"Starting download for skin: {skinName}");

        // First check if we have the texture cached
        Texture2D cachedTexture = LoadTextureFromFile(skinName);
        if (cachedTexture != null)
        {
            // Use cached texture
            skinTextures[skinName] = cachedTexture;
            ApplySkinToChessPieces(cachedTexture, skinName);
            Debug.Log($"Using cached skin: {skinName}");
            return;
        }

        string skinPath = $"{skinName}.png";

        // Create a reference to the file
        FirebaseStorage storage = FirebaseStorage.DefaultInstance;
        StorageReference mainRef = storage.GetReference(storagePath);
        StorageReference storageRef = mainRef.Child(skinPath);

        Debug.Log($"Downloading skin from: {storagePath}/{skinPath}");

        // Download the skin texture
        const long maxSize = 2 * 1024 * 1024; // 2MB max size
        storageRef.GetBytesAsync(maxSize).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"Failed to download skin: {skinName}. Error: {task.Exception}");

                // Create a fallback colored texture
                Texture2D fallbackTexture = CreateColorTexture(GetColorForSkin(skinName), 256, 256);

                // Cache the texture
                skinTextures[skinName] = fallbackTexture;

                // Apply the skin
                ApplySkinToChessPieces(fallbackTexture, skinName);

                // Save texture to persistent data
                SaveTextureToFile(fallbackTexture, skinName);

                Debug.Log($"Applied fallback skin for: {skinName}");
                return;
            }

            // Convert bytes to texture
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(task.Result);

            // Cache the texture
            skinTextures[skinName] = texture;

            // Apply the skin
            ApplySkinToChessPieces(texture, skinName);

            // Save texture to persistent data so it's available after restart
            SaveTextureToFile(texture, skinName);

            Debug.Log($"Successfully downloaded and applied skin: {skinName}");
        });
    }

    /// <summary>
    /// Applies a skin to chess pieces
    /// </summary>
    public void ApplySkin(string skinName)
    {
        Debug.Log($"ApplySkin called for skin: {skinName}");

        Texture2D skinTexture = null;

        // Check if we have the texture cached
        if (skinTextures.TryGetValue(skinName, out skinTexture))
        {
            // Apply from cache
            ApplySkinToChessPieces(skinTexture, skinName);
        }
        else
        {
            // Try to load from persistent storage
            skinTexture = LoadTextureFromFile(skinName);
            if (skinTexture != null)
            {
                // Cache and apply
                skinTextures[skinName] = skinTexture;
                ApplySkinToChessPieces(skinTexture, skinName);
            }
            else
            {
                // We don't have it yet, download it
                DownloadAndApplySkin(skinName);
            }
        }

        // If we're in a networked game, make sure we notify others about this skin 
        // (but only if it's not already in response to a notification)
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsConnectedClient &&
            ChessRelay.Instance != null)
        {
            // Notify about the skin purchase via the ChessRelay
            ChessRelay.Instance.NotifySkinPurchasedServerRpc(skinName);
        }
    }

    /// <summary>
    /// Applies a skin texture to chess pieces in the scene based on player role (host or client)
    /// </summary>
    /// <summary>
/// Applies a skin texture to chess pieces in the scene based on player role (host or client)
/// </summary>
private void ApplySkinToChessPieces(Texture2D skinTexture, string skinName)
{
    // Get color from the skin name
    Color pieceColor = GetColorForSkin(skinName);

    // Get all VisualPiece components in the scene
    VisualPiece[] visualPieces = FindObjectsOfType<VisualPiece>(true);
    
    // Determine if this is host (white pieces) or client (black pieces)
    bool isHost = IsHost();
    bool isNetworkedGame = Unity.Netcode.NetworkManager.Singleton != null && 
                          Unity.Netcode.NetworkManager.Singleton.IsConnectedClient;
    
    Debug.Log($"Applying skin: {skinName} as {(isHost ? "Host (White Pieces)" : "Client (Black Pieces)")} - Networked: {isNetworkedGame}");
    
    int piecesUpdated = 0;
    
    foreach (VisualPiece piece in visualPieces)
    {
        // Always accurately detect if this is a white piece
        bool isPieceWhite = IsPieceWhite(piece);
        
        // In multiplayer mode, apply host/client rules based on the VIEWING player's role
        if (isNetworkedGame)
        {
            // In networked game, we strictly follow these rules:
            // 1. Host can only change white pieces
            // 2. Client can only change black pieces
            if (isHost)
            {
                // Host is viewing, only affect WHITE pieces
                if (isPieceWhite)
                {
                    ApplyColorToPiece(piece, pieceColor);
                    piecesUpdated++;
                }
            }
            else
            {
                // Client is viewing, only affect BLACK pieces
                if (!isPieceWhite)
                {
                    ApplyColorToPiece(piece, pieceColor);
                    piecesUpdated++;
                }
            }
        }
        else
        {
            // In single-player mode, apply to all pieces
            ApplyColorToPiece(piece, pieceColor);
            piecesUpdated++;
        }
    }
    
    // Remember the selected skin for the appropriate side
    if (isNetworkedGame)
    {
        if (isHost)
        {
            // Host stores preference for white pieces
            PlayerPrefs.SetString("SelectedSkin_White", skinName);
        }
        else
        {
            // Client stores preference for black pieces
            PlayerPrefs.SetString("SelectedSkin_Black", skinName);
        }
    }
    else
    {
        // Single player mode, store for all pieces
        PlayerPrefs.SetString("SelectedSkin", skinName);
    }
    PlayerPrefs.Save();
    
    Debug.Log($"Applied skin: {skinName} to {piecesUpdated} {(isNetworkedGame ? (isHost ? "white" : "black") : "all")} chess pieces with color: {pieceColor}");
}

    /// <summary>
    /// Helper method to apply color to a piece
    /// </summary>
    private void ApplyColorToPiece(VisualPiece piece, Color color)
    {
        Renderer renderer = piece.GetComponent<Renderer>();
        if (renderer != null)
        {
            // If there's a main material, change its color
            Material mat = renderer.material;
            if (mat != null)
            {
                // Change color while preserving original material properties
                mat.color = color;
            }
        }
    }

    /// <summary>
    /// Determines if a piece is white based on various heuristics
    /// </summary>
    private bool IsPieceWhite(VisualPiece piece)
    {
        // The most reliable method: Use the PieceColor property from VisualPiece
        if (piece.PieceColor == Side.White)
        {
            return true;
        }
        else if (piece.PieceColor == Side.Black)
        {
            return false;
        }

        // Fallback methods if PieceColor isn't set correctly

        // Check the piece's name (if it contains "white" or "White")
        if (piece.name.Contains("white") || piece.name.Contains("White"))
        {
            return true;
        }
        else if (piece.name.Contains("black") || piece.name.Contains("Black"))
        {
            return false;
        }

        // Check the piece's position (white pieces typically start at rows 0-1)
        Vector3 position = piece.transform.position;
        if (position.z < 3.0f) // Assuming the board is oriented with white pieces at lower z coordinates
        {
            return true;
        }

        // Default: Assume it's black if we can't determine
        return false;
    }

    /// <summary>
    /// Determines if the current player is the host (controls white pieces)
    /// </summary>
    private bool IsHost()
    {
        // If using Unity's Netcode for GameObjects
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            return Unity.Netcode.NetworkManager.Singleton.IsHost ||
                   Unity.Netcode.NetworkManager.Singleton.IsServer;
        }

        // If using ChessNetworkManager
        if (ChessNetworkManager.Instance != null)
        {
            // The host plays White in the Chess game
            return ChessNetworkManager.Instance.GetPlayerSide(
                Unity.Netcode.NetworkManager.Singleton.LocalClientId) == Side.White;
        }

        // Default to true if no network is available (single player)
        return true;
    }

    /// <summary>
    /// Returns the appropriate color for a skin name
    /// </summary>
    private Color GetColorForSkin(string skinName)
    {
        switch (skinName.ToLower())
        {
            case "green":
                return new Color(0.2f, 0.8f, 0.2f);
            case "blue":
                return new Color(0.2f, 0.2f, 0.8f);
            case "red":
                return new Color(0.8f, 0.2f, 0.2f);
            case "pink":
                return new Color(0.8f, 0.4f, 0.8f);
            default:
                return Color.white;
        }
    }

    /// <summary>
    /// Creates a single-color texture for testing
    /// </summary>
    private Texture2D CreateColorTexture(Color color, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Saves a texture to persistent data storage
    /// </summary>
    private void SaveTextureToFile(Texture2D texture, string skinName)
    {
        // Convert texture to PNG bytes
        byte[] bytes = texture.EncodeToPNG();

        // Get persistent path
        string filePath = Path.Combine(Application.persistentDataPath, $"skin_{skinName}.png");

        // Write to file
        File.WriteAllBytes(filePath, bytes);

        Debug.Log($"Saved skin texture to: {filePath}");
    }

    /// <summary>
    /// Loads a texture from persistent data storage
    /// </summary>
    private Texture2D LoadTextureFromFile(string skinName)
    {
        string filePath = Path.Combine(Application.persistentDataPath, $"skin_{skinName}.png");

        if (!File.Exists(filePath))
        {
            if (debugMode)
                Debug.Log($"No saved skin texture found at: {filePath}");
            return null;
        }

        // Read bytes from file
        byte[] bytes = File.ReadAllBytes(filePath);

        // Create texture
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(bytes);

        Debug.Log($"Loaded skin texture from file: {filePath}");
        return texture;
    }

    /// <summary>
    /// Applies the previously selected skin when the game starts
    /// </summary>
    public void ApplySelectedSkin()
    {
        // In networked mode, don't auto-apply any skins at startup
        // This ensures pieces start with their original colors
        bool isNetworked = Unity.Netcode.NetworkManager.Singleton != null && 
                           Unity.Netcode.NetworkManager.Singleton.IsConnectedClient;
    
        if (isNetworked)
        {
            Debug.Log("Networked game detected - NOT auto-applying any skins at startup to preserve original colors");
            return;
        }
    
        // Only for single player, check if there's a selected skin
        string selectedSkin = PlayerPrefs.GetString("SelectedSkin", "");
    
        if (string.IsNullOrEmpty(selectedSkin) || !purchasedSkins.ContainsKey(selectedSkin) || !purchasedSkins[selectedSkin])
        {
            Debug.Log("No valid previously selected skin to apply");
            return;
        }
    
        // Single player mode - apply immediately
        Debug.Log($"Applying previously selected skin: {selectedSkin} (single player mode)");
        ApplySkin(selectedSkin);
    }

    private IEnumerator DelayedSkinApply(string skinName = null)
    {
        // Wait for network roles to be established
        yield return new WaitForSeconds(2.0f);

        // Use provided skin name or check preferences
        string selectedSkin = skinName;
        if (string.IsNullOrEmpty(selectedSkin))
        {
            selectedSkin = PlayerPrefs.GetString("SelectedSkin", "");
        }

        if (!string.IsNullOrEmpty(selectedSkin) && purchasedSkins.ContainsKey(selectedSkin) &&
            purchasedSkins[selectedSkin])
        {
            bool isHost = IsHost();
            Debug.Log($"Applying previously selected skin: {selectedSkin} (networked mode) - IsHost: {isHost}");

            // In networked mode, we need to apply the skin and notify others
            ApplySkin(selectedSkin);

            // Also explicitly notify about our skin via the ChessRelay to ensure synchronization
            if (Unity.Netcode.NetworkManager.Singleton.IsConnectedClient && ChessRelay.Instance != null)
            {
                ChessRelay.Instance.NotifySkinPurchasedServerRpc(selectedSkin);
                Debug.Log($"Notified network about user's skin preference: {selectedSkin}");
            }
        }
    }

    /// <summary>
    /// Checks if a skin is already purchased
    /// </summary>
    public bool IsSkinPurchased(string skinName)
    {
        if (purchasedSkins.TryGetValue(skinName, out bool isPurchased))
        {
            return isPurchased;
        }

        return false;
    }

    /// <summary>
    /// Gets a skin texture from cache or disk
    /// </summary>
    public bool GetSkinTexture(string skinName, out Texture2D skinTexture)
    {
        // First check cache
        if (skinTextures.TryGetValue(skinName, out skinTexture))
        {
            return true;
        }

        // Then try loading from disk
        skinTexture = LoadTextureFromFile(skinName);
        if (skinTexture != null)
        {
            // Cache for future use
            skinTextures[skinName] = skinTexture;
            return true;
        }

        // Not found
        return false;
    }

    /// <summary>
    /// Applies a skin directly without triggering network notifications
    /// </summary>
    public void ApplySkinDirectly(Texture2D skinTexture, string skinName)
    {
        if (skinTexture != null)
        {
            // Apply directly to avoid notification loop
            ApplySkinToChessPieces(skinTexture, skinName);

            // Remember the selected skin
            PlayerPrefs.SetString("SelectedSkin", skinName);
            PlayerPrefs.Save();
        }
    }
    
    /// <summary>
    /// Applies a skin that was purchased by the remote player
    /// </summary>
    /// <param name="skinName">Name of the skin to apply</param>
    /// <param name="isHostPurchaser">True if the host purchased this skin, false if client</param>
    public void ApplyRemoteSkin(string skinName, bool isHostPurchaser)
    {
        Debug.Log($"ApplyRemoteSkin called for: {skinName}, HostPurchased: {isHostPurchaser}");
    
        Texture2D skinTexture = null;
    
        // Check if we have the texture cached
        if (skinTextures.TryGetValue(skinName, out skinTexture))
        {
            // Apply from cache
            ApplyRemoteSkinToChessPieces(skinTexture, skinName, isHostPurchaser);
        }
        else
        {
            // Try to load from persistent storage
            skinTexture = LoadTextureFromFile(skinName);
            if (skinTexture != null)
            {
                // Cache and apply
                skinTextures[skinName] = skinTexture;
                ApplyRemoteSkinToChessPieces(skinTexture, skinName, isHostPurchaser);
            }
            else
            {
                // We don't have it yet, download it
                DownloadAndApplyRemoteSkin(skinName, isHostPurchaser);
            }
        }
    }
    /// <summary>
/// Downloads a skin from Firebase Storage and applies it for a remote player
/// </summary>
private void DownloadAndApplyRemoteSkin(string skinName, bool isHostPurchaser)
{
    if (debugMode)
        Debug.Log($"Starting download for remote skin: {skinName}");
        
    // First check if we have the texture cached
    Texture2D cachedTexture = LoadTextureFromFile(skinName);
    if (cachedTexture != null)
    {
        // Use cached texture
        skinTextures[skinName] = cachedTexture;
        ApplyRemoteSkinToChessPieces(cachedTexture, skinName, isHostPurchaser);
        Debug.Log($"Using cached skin for remote player: {skinName}");
        return;
    }
    
    string skinPath = $"{skinName}.png";
    
    // Create a reference to the file
    FirebaseStorage storage = FirebaseStorage.DefaultInstance;
    StorageReference mainRef = storage.GetReference(storagePath);
    StorageReference storageRef = mainRef.Child(skinPath);
    
    Debug.Log($"Downloading remote skin from: {storagePath}/{skinPath}");
    
    // Download the skin texture
    const long maxSize = 2 * 1024 * 1024; // 2MB max size
    storageRef.GetBytesAsync(maxSize).ContinueWithOnMainThread(task => {
        if (task.IsFaulted || task.IsCanceled)
        {
            Debug.LogError($"Failed to download remote skin: {skinName}. Error: {task.Exception}");
            
            // Create a fallback colored texture
            Texture2D fallbackTexture = CreateColorTexture(GetColorForSkin(skinName), 256, 256);
            
            // Cache the texture
            skinTextures[skinName] = fallbackTexture;
            
            // Apply the skin
            ApplyRemoteSkinToChessPieces(fallbackTexture, skinName, isHostPurchaser);
            
            // Save texture to persistent data
            SaveTextureToFile(fallbackTexture, skinName);
            
            Debug.Log($"Applied fallback remote skin for: {skinName}");
            return;
        }

        // Convert bytes to texture
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(task.Result);
        
        // Cache the texture
        skinTextures[skinName] = texture;
        
        // Apply the skin
        ApplyRemoteSkinToChessPieces(texture, skinName, isHostPurchaser);
        
        // Save texture to persistent data so it's available after restart
        SaveTextureToFile(texture, skinName);
        
        Debug.Log($"Successfully downloaded and applied remote skin: {skinName}");
    });
}
    /// <summary>
    /// Applies a remote player's skin purchase to the appropriate pieces
    /// </summary>
    private void ApplyRemoteSkinToChessPieces(Texture2D skinTexture, string skinName, bool isHostPurchaser)
{
    // Get color from the skin name
    Color pieceColor = GetColorForSkin(skinName);

    // Get all VisualPiece components in the scene
    VisualPiece[] visualPieces = FindObjectsOfType<VisualPiece>(true);

    // Determine local player's status
    bool isLocalHost = IsHost();
    bool isNetworkedGame = Unity.Netcode.NetworkManager.Singleton != null && 
                           Unity.Netcode.NetworkManager.Singleton.IsConnectedClient;

    Debug.Log($"Applying remote skin: {skinName}, PurchasedByHost:{isHostPurchaser}, LocalIsHost:{isLocalHost}");

    int piecesUpdated = 0;

    foreach (VisualPiece piece in visualPieces)
    {
        bool isPieceWhite = IsPieceWhite(piece);

        // Strict conditions for applying skin
        if (isNetworkedGame)
        {
            // If HOST purchased the skin
            if (isHostPurchaser)
            {
                // Local host should change WHITE pieces
                // Local client should change BLACK pieces
                if ((isLocalHost && isPieceWhite) || (!isLocalHost && !isPieceWhite))
                {
                    ApplyColorToPiece(piece, pieceColor);
                    piecesUpdated++;
                }
            }
            // If CLIENT purchased the skin
            else
            {
                // Local host should change BLACK pieces
                // Local client should change WHITE pieces
                if ((isLocalHost && !isPieceWhite) || (!isLocalHost && isPieceWhite))
                {
                    ApplyColorToPiece(piece, pieceColor);
                    piecesUpdated++;
                }
            }
        }
    }

    // Store the skin preference (for the appropriate side)
    if (isHostPurchaser)
    {
        // Host purchased for WHITE pieces
        PlayerPrefs.SetString("SelectedSkin_White", skinName);
    }
    else
    {
        // Client purchased for BLACK pieces
        PlayerPrefs.SetString("SelectedSkin_Black", skinName);
    }
    PlayerPrefs.Save();

    Debug.Log($"Applied remote skin: {skinName} to {piecesUpdated} pieces");
}

    /// <summary>
    /// Used for debugging to mark all skins as purchased
    /// </summary>
    public void DEBUG_PurchaseAllSkins()
    {
        foreach (string skinName in skinNames)
        {
            purchasedSkins[skinName] = true;
            PlayerPrefs.SetInt("Skin_" + skinName, 1);
        }

        PlayerPrefs.Save();

        // Refresh UI
        for (int i = 0; i < skinNames.Length && i < skinButtons.Length; i++)
        {
            UpdateSkinButtonVisual(i);
        }

        Debug.Log("DEBUG: All skins marked as purchased");
    }
}