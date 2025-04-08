using UnityEngine;
using System;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using Firebase;
using Firebase.Extensions;

public class FirebaseInitializer : MonoBehaviour
{
    public static event Action FirebaseInitialized;
    public static bool IsFirebaseReady { get; private set; }
    
    // Changed from TextAsset to string to make it easier
    [SerializeField] private string plistFileName = "GoogleService-Info-2.plist";
    [SerializeField] private bool debugMode = true;

    private void Awake()
    {
        IsFirebaseReady = false;
        
        // Set Firebase debug level if needed
        if (debugMode)
        {
            Firebase.FirebaseApp.LogLevel = Firebase.LogLevel.Debug;
            Debug.Log("Firebase debug logging enabled");
        }
        
        // Try to find the plist file in multiple locations
        TryConvertPlistToJson();
        
        InitializeFirebase();
    }
    
    private void TryConvertPlistToJson()
    {
        Debug.Log("Looking for Firebase configuration file...");
        
        // Try to find the file in various locations
        string plistContent = null;
        
        // Option 1: Try loading from StreamingAssets
        string streamingPath = Path.Combine(Application.streamingAssetsPath, plistFileName);
        if (File.Exists(streamingPath))
        {
            Debug.Log($"Found plist at StreamingAssets: {streamingPath}");
            plistContent = File.ReadAllText(streamingPath);
        }
        
        // Option 2: Try loading from Resources folder
        if (plistContent == null)
        {
            string resourceName = plistFileName.Replace(".plist", "");
            TextAsset textAsset = Resources.Load<TextAsset>(resourceName);
            if (textAsset != null)
            {
                Debug.Log($"Found plist in Resources: {resourceName}");
                plistContent = textAsset.text;
            }
        }
        
        // Option 3: Try loading from Assets/StreamingAssets as .txt
        if (plistContent == null)
        {
            string txtPath = Path.Combine(Application.streamingAssetsPath, plistFileName + ".txt");
            if (File.Exists(txtPath))
            {
                Debug.Log($"Found plist as txt: {txtPath}");
                plistContent = File.ReadAllText(txtPath);
            }
        }
        
        // Option 4: Try finding it directly in the project
        if (plistContent == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("GoogleService-Info-2");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                Debug.Log($"Found plist using AssetDatabase: {path}");
                TextAsset asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                {
                    plistContent = asset.text;
                }
            }
        }
        
        // If we found the file, convert it
        if (plistContent != null)
        {
            try
            {
                ConvertPlistToJson(plistContent);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error converting plist: {e.Message}\n{e.StackTrace}");
            }
        }
        else
        {
            Debug.LogError($"Could not find GoogleService-Info-2.plist file in any location!");
        }
    }

    private void ConvertPlistToJson(string plistContent)
    {
        Debug.Log("Converting plist to JSON...");
        
        try
        {
            XDocument plistDoc = XDocument.Parse(plistContent);
            if (plistDoc == null || plistDoc.Root == null)
            {
                Debug.LogError("Failed to parse plist: document or root is null");
                return;
            }
            
            XElement dictElement = plistDoc.Root.Element("dict");
            if (dictElement == null)
            {
                Debug.LogError("Failed to find dict element in plist");
                return;
            }

            // Ensure StreamingAssets directory exists
            Directory.CreateDirectory(Application.streamingAssetsPath);

            string jsonPath = Path.Combine(Application.streamingAssetsPath, "google-services-desktop.json");
            string jsonContent = GenerateJsonContent(dictElement);
            
            File.WriteAllText(jsonPath, jsonContent);
            Debug.Log($"Created Firebase config JSON at: {jsonPath}");
            
            // Also create in alternative locations that Firebase might check
            string altPath1 = Path.Combine(Application.streamingAssetsPath, "google-services.json");
            File.WriteAllText(altPath1, jsonContent);
            Debug.Log($"Created alternate Firebase config at: {altPath1}");
            
            // Create in the persistent data path too
            string persistentPath = Path.Combine(Application.persistentDataPath, "google-services-desktop.json");
            Directory.CreateDirectory(Path.GetDirectoryName(persistentPath));
            File.WriteAllText(persistentPath, jsonContent);
            Debug.Log($"Created persistent Firebase config at: {persistentPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ConvertPlistToJson: {e.Message}\n{e.StackTrace}");
        }
    }

    private string GenerateJsonContent(XElement dictElement)
    {
        string projectNumber = GetPlistStringValue(dictElement, "GCM_SENDER_ID");
        string projectId = GetPlistStringValue(dictElement, "PROJECT_ID");
        string storageBucket = GetPlistStringValue(dictElement, "STORAGE_BUCKET");
        string apiKey = GetPlistStringValue(dictElement, "API_KEY");
        string googleAppId = GetPlistStringValue(dictElement, "GOOGLE_APP_ID");
        string bundleId = GetPlistStringValue(dictElement, "BUNDLE_ID");

        Debug.Log($"Generated config with: Project ID: {projectId}, Storage: {storageBucket}");

        return $@"{{
    ""project_info"": {{
        ""project_number"": ""{projectNumber}"",
        ""project_id"": ""{projectId}"",
        ""storage_bucket"": ""{storageBucket}""
    }},
    ""client"": [
        {{
            ""client_info"": {{
                ""mobilesdk_app_id"": ""{googleAppId}"",
                ""android_client_info"": {{
                    ""package_name"": ""{bundleId}""
                }}
            }},
            ""api_key"": [
                {{
                    ""current_key"": ""{apiKey}""
                }}
            ],
            ""services"": {{
                ""appinvite_service"": {{
                    ""other_platform_oauth_client"": []
                }}
            }}
        }}
    ],
    ""configuration_version"": ""1""
}}";
    }

    private string GetPlistStringValue(XElement dictElement, string key)
    {
        var keyElement = dictElement.Elements("key").FirstOrDefault(k => k.Value == key);
        if (keyElement != null)
        {
            var nextElement = keyElement.NextNode as XElement;
            return nextElement?.Value ?? "";
        }
        return "";
    }

    private void InitializeFirebase()
    {
        Debug.Log("Starting Firebase initialization...");
        
        try
        {
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
                try
                {
                    var dependencyStatus = task.Result;
                    if (dependencyStatus == DependencyStatus.Available)
                    {
                        FirebaseApp app = FirebaseApp.DefaultInstance;
                        if (app == null)
                        {
                            Debug.Log("Creating new Firebase app instance");
                            app = FirebaseApp.Create();
                        }
                        
                        Debug.Log($"Firebase app name: {app.Name}");
                        IsFirebaseReady = true;
                        Debug.Log("Firebase initialized successfully!");
                        FirebaseInitialized?.Invoke();
                    }
                    else
                    {
                        Debug.LogError($"Could not resolve Firebase dependencies: {dependencyStatus}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Exception during Firebase initialization: {e.Message}\n{e.StackTrace}");
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception starting Firebase initialization: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Add a public method to allow manual initialization from the Inspector or button
    public void ManualInitialize()
    {
        Debug.Log("Manual Firebase initialization requested");
        TryConvertPlistToJson();
        InitializeFirebase();
    }
}