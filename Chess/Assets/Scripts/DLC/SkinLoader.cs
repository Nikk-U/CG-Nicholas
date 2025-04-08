using UnityEngine;
using System;
using System.Collections.Generic;
using Firebase.Storage;
using Firebase.Extensions;
using System.IO;

public class SkinLoader : MonoBehaviour
{
    public static SkinLoader Instance { get; private set; }

    [SerializeField] private string storagePath = "gs://nicholasdlc.firebasestorage.app/Chess_Skins/";
    
    public event Action<string> OnSkinDownloaded;
    
    private Dictionary<string, Texture2D> skinCache = new Dictionary<string, Texture2D>();
    private Dictionary<string, Color> skinColors = new Dictionary<string, Color>
    {
        { "Green", new Color(0.2f, 0.8f, 0.2f) },
        { "Blue", new Color(0.2f, 0.2f, 0.8f) },
        { "Red", new Color(0.8f, 0.2f, 0.2f) },
        { "Pink", new Color(0.8f, 0.4f, 0.8f) }
    };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // New GetSkin method to retrieve skin texture
    public Texture2D GetSkin(string skinName)
    {
        // Check if skin is cached
        if (skinCache.TryGetValue(skinName, out Texture2D cachedTexture))
        {
            return cachedTexture;
        }
        
        // Try to load from disk
        Texture2D localTexture = LoadSkinFromDisk(skinName);
        if (localTexture != null)
        {
            skinCache[skinName] = localTexture;
            return localTexture;
        }
        
        // If no cached or local texture, return null
        return null;
    }

    public void DownloadSkin(string skinName, Action<Texture2D> onComplete = null)
    {
        if (skinCache.TryGetValue(skinName, out Texture2D cachedTexture))
        {
            onComplete?.Invoke(cachedTexture);
            return;
        }
        
        Texture2D localTexture = LoadSkinFromDisk(skinName);
        if (localTexture != null)
        {
            skinCache[skinName] = localTexture;
            onComplete?.Invoke(localTexture);
            return;
        }
        
        string skinPath = $"{storagePath}{skinName}.png";
        
        try
        {
            StorageReference storageRef = FirebaseStorage.DefaultInstance.GetReferenceFromUrl(skinPath);
            
            const long maxSize = 1 * 1024 * 1024; // 1MB max size
            storageRef.GetBytesAsync(maxSize).ContinueWithOnMainThread(task => {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError($"Failed to download skin: {skinName}. Error: {task.Exception}");
                    onComplete?.Invoke(null);
                    return;
                }
                
                byte[] bytes = task.Result;
                
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(bytes);
                
                skinCache[skinName] = texture;
                
                SaveSkinToDisk(skinName, bytes);
                
                OnSkinDownloaded?.Invoke(skinName);
                
                onComplete?.Invoke(texture);
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Error downloading skin: {e.Message}");
            onComplete?.Invoke(null);
        }
    }

    public void ApplySkin(string skinName)
    {
        // Retrieve the color for the chosen skin
        if (!skinColors.TryGetValue(skinName, out Color pieceColor))
        {
            Debug.LogWarning($"Unknown skin color for {skinName}. Using default white.");
            pieceColor = Color.white;
        }

        // Find all visual pieces in the scene
        VisualPiece[] visualPieces = FindObjectsOfType<VisualPiece>(true);
        
        foreach (VisualPiece piece in visualPieces)
        {
            Renderer renderer = piece.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = renderer.material;
                if (mat != null)
                {
                    // Change piece color
                    mat.color = pieceColor;
                }
            }
        }
        
        // Save the selected skin preference
        PlayerPrefs.SetString("SelectedSkin", skinName);
        PlayerPrefs.Save();
        
        Debug.Log($"Applied skin '{skinName}' to all chess pieces");
    }

    private void SaveSkinToDisk(string skinName, byte[] bytes)
    {
        try
        {
            string filePath = Path.Combine(Application.persistentDataPath, $"skin_{skinName}.png");
            File.WriteAllBytes(filePath, bytes);
            Debug.Log($"Saved skin to disk: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving skin to disk: {e.Message}");
        }
    }

    private Texture2D LoadSkinFromDisk(string skinName)
    {
        string filePath = Path.Combine(Application.persistentDataPath, $"skin_{skinName}.png");
        
        if (!File.Exists(filePath))
        {
            return null;
        }
        
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(bytes);
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading skin from disk: {e.Message}");
            return null;
        }
    }
}