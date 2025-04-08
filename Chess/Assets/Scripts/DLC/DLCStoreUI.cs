using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DLC
{
    /// <summary>
    /// Handles the UI for the DLC Store, showing available skins and handling user interaction.
    /// Attach this to the DLC Store panel in your Canvas.
    /// </summary>
    public class DLCStoreUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform skinButtonsContainer;
        [SerializeField] private GameObject skinButtonPrefab;
        
        [Header("Skin Configuration")]
        [SerializeField] private string[] skinNames = { "Green", "Blue", "Red", "Pink" };
        [SerializeField] private float[] skinPrices = { 1.99f, 2.99f, 2.99f, 3.99f };
        [SerializeField] private bool useDirectButtons = true; // Set to false if you want to use prefab buttons
        
        private List<GameObject> spawnedButtons = new List<GameObject>();
        
        private void Start()
        {
            // Set up the close button
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(() => gameObject.SetActive(false));
            }
            
            // Create skin buttons
            if (!useDirectButtons || skinButtonPrefab != null)
            {
                CreateSkinButtons();
            }
        }

        /// <summary>
        /// Creates the UI buttons for each available skin
        /// </summary>
        private void CreateSkinButtons()
        {
            // Skip if we're using direct buttons
            if (useDirectButtons || skinButtonsContainer == null || skinButtonPrefab == null)
            {
                return;
            }
            
            // Clear any existing buttons
            foreach (GameObject button in spawnedButtons)
            {
                Destroy(button);
            }
            spawnedButtons.Clear();
            
            // Create new buttons for each skin
            for (int i = 0; i < skinNames.Length; i++)
            {
                GameObject buttonObj = Instantiate(skinButtonPrefab, skinButtonsContainer);
                spawnedButtons.Add(buttonObj);
                
                // Set skin name
                Text nameText = buttonObj.transform.Find("NameText")?.GetComponent<Text>();
                if (nameText != null)
                    nameText.text = skinNames[i];
                    
                // Set skin price
                Text priceText = buttonObj.transform.Find("PriceText")?.GetComponent<Text>();
                if (priceText != null)
                    priceText.text = $"${skinPrices[i]:F2}";
                    
                // Set button action
                Button button = buttonObj.GetComponent<Button>();
                if (button != null)
                {
                    int index = i; // Capture the index for the lambda
                    button.onClick.AddListener(() => OnSkinButtonClicked(index));
                }
                
                // Check if this skin is already purchased
                bool isPurchased = PlayerPrefs.GetInt("Skin_" + skinNames[i], 0) == 1;
                UpdateButtonVisual(buttonObj, isPurchased);
                
                // Load preview image
                Image previewImage = buttonObj.transform.Find("Preview")?.GetComponent<Image>();
                if (previewImage != null)
                {
                    LoadPreviewImage(skinNames[i], previewImage);
                }
            }
        }

        /// <summary>
        /// Loads a preview image for a skin from Firebase Storage or local cache
        /// </summary>
        private void LoadPreviewImage(string skinName, Image previewImage)
        {
            // Try to load from SkinLoader first
            if (SkinLoader.Instance != null)
            {
                Texture2D texture = SkinLoader.Instance.GetSkin(skinName);
                if (texture != null)
                {
                    previewImage.sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    return;
                }
            }
            
            // If not available in SkinLoader, request from DLCStoreManager
            if (DLCStoreManager.Instance != null)
            {
                // The DLCStoreManager will handle loading the preview
                DLCStoreManager.Instance.LoadSkinPreview(skinName, (sprite) => {
                    if (sprite != null && previewImage != null)
                    {
                        previewImage.sprite = sprite;
                    }
                });
            }
        }

        /// <summary>
        /// Updates the visual appearance of a skin button based on purchase status
        /// </summary>
        private void UpdateButtonVisual(GameObject buttonObj, bool isPurchased)
        {
            // Get the button component
            Button button = buttonObj.GetComponent<Button>();
            if (button == null) return;
            
            // Get the price text
            Text priceText = buttonObj.transform.Find("PriceText")?.GetComponent<Text>();
            
            if (isPurchased)
            {
                // Update color to indicate it's purchased
                ColorBlock colors = button.colors;
                colors.normalColor = new Color(0.7f, 1.0f, 0.7f);
                button.colors = colors;
                
                // Update price text to show "OWNED"
                if (priceText != null)
                    priceText.text = "OWNED";
            }
            else
            {
                // Default button color
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                button.colors = colors;
            }
        }

        /// <summary>
        /// Handles the click event for a skin button
        /// </summary>
        private void OnSkinButtonClicked(int skinIndex)
        {
            if (skinIndex < 0 || skinIndex >= skinNames.Length)
                return;
                
            string skinName = skinNames[skinIndex];
            float skinPrice = skinPrices[skinIndex];
            
            // Check if already purchased
            bool isPurchased = PlayerPrefs.GetInt("Skin_" + skinName, 0) == 1;
            
            if (isPurchased)
            {
                // Already purchased, just apply the skin
                ApplySkin(skinName);
                return;
            }
            
            // Otherwise, start the purchase process
            PurchaseSkin(skinName, skinPrice, skinIndex);
        }

        /// <summary>
        /// Starts the purchase process for a skin
        /// </summary>
        private void PurchaseSkin(string skinName, float skinPrice, int skinIndex)
        {
            Debug.Log($"Attempting to purchase skin: {skinName} for ${skinPrice:F2}");
            
            // In a real app, you would integrate with a payment API here
            // For demonstration purposes, we'll simulate a successful purchase
            
            // Display purchase confirmation dialog
            if (DLCStoreManager.Instance != null)
            {
                DLCStoreManager.Instance.ShowPurchaseConfirmation(skinName, skinPrice, () => {
                    // On confirm purchase
                    CompletePurchase(skinName, skinIndex);
                });
            }
            else
            {
                // If no manager exists, just complete the purchase immediately
                CompletePurchase(skinName, skinIndex);
            }
        }

        /// <summary>
        /// Completes a successful purchase
        /// </summary>
        private void CompletePurchase(string skinName, int skinIndex)
        {
            // Mark as purchased in PlayerPrefs
            PlayerPrefs.SetInt("Skin_" + skinName, 1);
            PlayerPrefs.Save();
            
            // Update the button visual if using prefabs
            if (!useDirectButtons && skinIndex < spawnedButtons.Count)
            {
                UpdateButtonVisual(spawnedButtons[skinIndex], true);
            }
            
            // Download and apply the skin
            if (SkinLoader.Instance != null)
            {
                SkinLoader.Instance.DownloadSkin(skinName, (texture) => {
                    if (texture != null)
                    {
                        SkinLoader.Instance.ApplySkin(skinName);
                    }
                });
            }
            else if (DLCStoreManager.Instance != null)
            {
                DLCStoreManager.Instance.ApplySkin(skinName);
            }
            
            Debug.Log($"Successfully purchased skin: {skinName}");
        }

        /// <summary>
        /// Applies a skin that has already been purchased
        /// </summary>
        private void ApplySkin(string skinName)
        {
            Debug.Log($"Applying purchased skin: {skinName}");
            
            // Use SkinLoader to apply the skin
            if (SkinLoader.Instance != null)
            {
                SkinLoader.Instance.ApplySkin(skinName);
            }
            else if (DLCStoreManager.Instance != null)
            {
                // Fallback to DLCStoreManager
                DLCStoreManager.Instance.ApplySkin(skinName);
            }
        }
        
        // Debugging method to purchase all skins
        public void DEBUG_PurchaseAllSkins()
        {
            for (int i = 0; i < skinNames.Length; i++)
            {
                PlayerPrefs.SetInt("Skin_" + skinNames[i], 1);
            }
            PlayerPrefs.Save();
            
            Debug.Log("DEBUG: All skins marked as purchased");
            
            // Update button visuals if using prefabs
            if (!useDirectButtons)
            {
                for (int i = 0; i < spawnedButtons.Count; i++)
                {
                    UpdateButtonVisual(spawnedButtons[i], true);
                }
            }
            
            // Notify DLCStoreManager if available
            if (DLCStoreManager.Instance != null)
            {
                DLCStoreManager.Instance.DEBUG_PurchaseAllSkins();
            }
        }
    }
}