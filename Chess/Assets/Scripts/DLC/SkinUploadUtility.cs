using UnityEngine;
using System.Collections;
using Firebase.Storage;
using Firebase.Extensions;
using System;
using System.IO;

namespace DLC
{
    public class SkinUploadUtility : MonoBehaviour
    {
        [SerializeField] private Texture2D[] skinTextures;
        [SerializeField] private string[] skinNames = { "Green", "Blue", "Red", "Pink" };
        [SerializeField] private string storagePath = "Chess_Skins";
        
        // Create default textures for testing if none are assigned
        [SerializeField] private bool createDefaultTextures = true;
        [SerializeField] private bool debugWithTestTexture = true;
        [SerializeField] private bool uploadOnStart = false;
        
        private bool hasInitialized = false;

        private void Start()
        {
            // Subscribe to the Firebase initialized event if it's not ready yet
            if (!FirebaseInitializer.IsFirebaseReady)
            {
                FirebaseInitializer.FirebaseInitialized += OnFirebaseInitialized;
            }
            else
            {
                InitializeTextures();
                hasInitialized = true;
                
                if (uploadOnStart)
                {
                    UploadSkins();
                }
            }
        }
        
        private void OnFirebaseInitialized()
        {
            Debug.Log("Firebase is now initialized in SkinUploadUtility");
            // Unsubscribe to avoid multiple calls
            FirebaseInitializer.FirebaseInitialized -= OnFirebaseInitialized;
            
            InitializeTextures();
            hasInitialized = true;
            
            if (uploadOnStart)
            {
                UploadSkins();
            }
        }
        
        private void InitializeTextures()
        {
            if (createDefaultTextures && (skinTextures == null || skinTextures.Length == 0))
            {
                Debug.Log("Creating default skin textures for testing");
                
                // Create default textures with different colors
                skinTextures = new Texture2D[4];
                
                // Green skin
                skinTextures[0] = CreateColorTexture(new Color(0.2f, 0.8f, 0.2f), 256, 256);
                
                // Blue skin
                skinTextures[1] = CreateColorTexture(new Color(0.2f, 0.2f, 0.8f), 256, 256);
                
                // Red skin
                skinTextures[2] = CreateColorTexture(new Color(0.8f, 0.2f, 0.2f), 256, 256);
                
                // Pink skin
                skinTextures[3] = CreateColorTexture(new Color(0.8f, 0.4f, 0.8f), 256, 256);
            }
        }
        
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

        public void UploadSkins()
        {
            // Make sure we have textures (create them if needed)
            if (!hasInitialized)
            {
                InitializeTextures();
            }
            
            // Validate textures
            if (skinTextures == null || skinTextures.Length == 0)
            {
                Debug.LogError("No skin textures assigned for upload!");
                return;
            }

            // Check Firebase readiness
            if (!FirebaseInitializer.IsFirebaseReady)
            {
                Debug.LogWarning("Firebase is not initialized. Please wait and try again.");
                return;
            }

            StartCoroutine(UploadSkinsCoroutine());
        }

        private IEnumerator UploadSkinsCoroutine()
        {
            // If debug mode, upload a test texture first
            if (debugWithTestTexture)
            {
                yield return UploadTestTexture();
                yield return new WaitForSeconds(0.5f);
            }
            
            for (int i = 0; i < skinTextures.Length; i++)
            {
                if (skinTextures[i] == null) continue;

                string skinName = i < skinNames.Length ? skinNames[i] : $"Skin_{i}";
                
                Debug.Log($"Starting upload for skin: {skinName}");
                
                // Full skin texture upload
                yield return UploadTexture(skinName, skinTextures[i], false);
                
                // Preview texture upload (resized)
                Texture2D previewTexture = ResizeTexture(skinTextures[i], 128, 128);
                yield return UploadTexture(skinName + "_preview", previewTexture, true);
                
                // Small delay between uploads to avoid overwhelming Firebase
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("All skin uploads completed!");
        }
        
        private IEnumerator UploadTestTexture()
        {
            Debug.Log("Uploading test texture to verify Storage access...");
            
            // Create a distinctive test pattern
            Texture2D testTexture = new Texture2D(64, 64);
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    if ((x / 8 + y / 8) % 2 == 0)
                        testTexture.SetPixel(x, y, Color.black);
                    else
                        testTexture.SetPixel(x, y, Color.yellow);
                }
            }
            testTexture.Apply();
            
            // Upload to Chess_Skins folder
            yield return UploadTexture("test", testTexture, false);
        }

        private IEnumerator UploadTexture(string fileName, Texture2D texture, bool isPreview)
        {
            string errorMessage = null;
            bool uploadComplete = false;
            
            try
            {
                // Convert texture to PNG bytes
                byte[] bytes = texture.EncodeToPNG();
                
                // Also save locally for debugging
                try
                {
                    string localPath = Path.Combine(Application.persistentDataPath, $"{fileName}.png");
                    File.WriteAllBytes(localPath, bytes);
                    Debug.Log($"Saved local copy for debug: {localPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not save local copy: {e.Message}");
                }
                
                // Create a reference to the file location
                FirebaseStorage storage = FirebaseStorage.DefaultInstance;
                
                // Make sure the path is correct
                string filePath = $"{fileName}.png";
                
                // Create the reference properly
                StorageReference mainRef = storage.GetReference(storagePath);
                StorageReference fileRef = mainRef.Child(filePath);
                
                Debug.Log($"Attempting to upload to: {storagePath}/{filePath}");
                
                // Upload task
                var uploadTask = fileRef.PutBytesAsync(bytes);
                
                uploadTask.ContinueWithOnMainThread(task => 
                {
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        if (task.Exception != null)
                        {
                            // Get more detailed error info
                            Exception baseEx = task.Exception.GetBaseException();
                            errorMessage = $"Failed to upload {fileName}: {baseEx.GetType().Name}: {baseEx.Message}";
                            
                            // If it's a StorageException, get more info
                            if (baseEx is Firebase.Storage.StorageException storageEx)
                            {
                                errorMessage += $" (ErrorCode: {storageEx.ErrorCode})";
                            }
                        }
                        else
                        {
                            errorMessage = $"Failed to upload {fileName}: Unknown error";
                        }
                    }
                    else
                    {
                        Debug.Log($"Successfully uploaded {fileName}.png to {fileRef.Path}");
                        
                        // Try to get the download URL
                        fileRef.GetDownloadUrlAsync().ContinueWithOnMainThread(urlTask => {
                            if (!urlTask.IsFaulted && !urlTask.IsCanceled)
                            {
                                Debug.Log($"Download URL: {urlTask.Result}");
                            }
                        });
                    }
                    uploadComplete = true;
                });
            }
            catch (Exception e)
            {
                errorMessage = $"Error in UploadTexture: {e.Message}\n{e.StackTrace}";
                uploadComplete = true;
            }
            
            // Wait for upload to complete
            yield return new WaitUntil(() => uploadComplete);
            
            // Log any errors
            if (!string.IsNullOrEmpty(errorMessage))
            {
                Debug.LogError(errorMessage);
            }
        }
        
        private Texture2D ResizeTexture(Texture2D originalTexture, int targetWidth, int targetHeight)
        {
            RenderTexture rt = new RenderTexture(targetWidth, targetHeight, 24);
            RenderTexture.active = rt;
            Graphics.Blit(originalTexture, rt);
            Texture2D result = new Texture2D(targetWidth, targetHeight);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            RenderTexture.active = null;
            Destroy(rt);
            return result;
        }
        
        // Simple test method you can call from the Inspector
        public void TestUpload()
        {
            if (!FirebaseInitializer.IsFirebaseReady)
            {
                Debug.LogError("Firebase not ready yet!");
                return;
            }
            
            Debug.Log("Starting test upload...");
            
            // Create a simple red texture for testing
            Texture2D testTexture = new Texture2D(64, 64);
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    testTexture.SetPixel(x, y, Color.red);
                }
            }
            testTexture.Apply();
            
            StartCoroutine(UploadTexture("test", testTexture, false));
        }
        
        // Debug method to fix a specific issue with missing permissions
        public void FixPermissions()
        {
            try
            {
                string heartbeatDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "firebase-heartbeat"
                );
                
                if (!Directory.Exists(heartbeatDir))
                {
                    Directory.CreateDirectory(heartbeatDir);
                    Debug.Log($"Created directory: {heartbeatDir}");
                }
                else
                {
                    Debug.Log($"Directory already exists: {heartbeatDir}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error fixing permissions: {e.Message}");
            }
        }
    }
}