using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Analytics;
using Unity.Netcode;
using UnityChess;

public class FirebaseAnalyticsManager : MonoBehaviour
{
    [Header("Firebase References")]
    private FirebaseApp app;
    private DatabaseReference databaseRef;
    private bool isInitialized = false;
    
    [Header("UI Elements")]
    [SerializeField] private GameObject analyticsPanel;
    [SerializeField] private TMP_InputField saveNameInput;
    [SerializeField] private TMP_Dropdown loadGameDropdown;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI matchesPlayedText;
    [SerializeField] private TextMeshProUGUI whiteWinsText;
    [SerializeField] private TextMeshProUGUI blackWinsText;
    
    // Data storage
    private Dictionary<string, string> savedGames = new Dictionary<string, string>();
    private int totalMatches = 0;
    private int whiteWins = 0;
    private int blackWins = 0;
    
    // Singleton
    public static FirebaseAnalyticsManager Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Hide analytics panel initially
        if (analyticsPanel != null)
            analyticsPanel.SetActive(false);
    }
    
    private void Start()
    {
        // Start Firebase initialization
        InitializeFirebase();
        
        // Subscribe to game events
        if (GameManager.Instance != null)
        {
            GameManager.NewGameStartedEvent += OnGameStarted;
            GameManager.GameEndedEvent += OnGameEnded;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.NewGameStartedEvent -= OnGameStarted;
            GameManager.GameEndedEvent -= OnGameEnded;
        }
    }
    
    private void InitializeFirebase()
    {
        // Display status
        SetStatus("Initializing Firebase...");
        
        // First make sure dependencies are available
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            if (task.Result == DependencyStatus.Available)
            {
                // Initialize the Firebase app
                try 
                {
                    FirebaseApp app = FirebaseApp.DefaultInstance;
                    if (app == null)
                    {
                        // Create with explicit database URL
                        AppOptions options = new AppOptions();
                        options.DatabaseUrl = new Uri("https://nicholasdlc-default-rtdb.europe-west1.firebasedatabase.app/");
                        app = FirebaseApp.Create(options);
                    }
                    
                    // Get database with explicit URL
                    FirebaseDatabase database = FirebaseDatabase.GetInstance(app, "https://nicholasdlc-default-rtdb.europe-west1.firebasedatabase.app/");
                    databaseRef = database.RootReference;
                    
                    // Mark as initialized
                    isInitialized = true;
                    
                    // Initialize analytics
                    FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                    
                    // Update UI
                    UnityEngine.Debug.Log("Firebase initialized successfully!");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        SetStatus("Firebase initialized successfully");
                        
                        // Load initial data
                        LoadData();
                    });
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Firebase initialization error: {ex.Message}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        SetStatus("Firebase initialization failed");
                    });
                }
            }
            else
            {
                UnityEngine.Debug.LogError("Firebase dependency check failed: " + task.Result);
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    SetStatus("Firebase dependency check failed");
                });
            }
        });
    }
    
    private void OnGameStarted()
    {
        // Log game start event
        if (isInitialized)
        {
            FirebaseAnalytics.LogEvent("game_started", 
                new Parameter("timestamp", DateTime.UtcNow.ToString("o")));
        }
    }
    
    private void OnGameEnded()
    {
        if (!isInitialized) return;
        
        // Determine game result
        bool hasLatestMove = GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        string result = "draw";
        
        if (hasLatestMove && latestHalfMove.CausedCheckmate)
        {
            if (latestHalfMove.Piece.Owner == Side.White)
            {
                UpdateStats(true); // White wins
                result = "white_win";
            }
            else
            {
                UpdateStats(false); // Black wins
                result = "black_win";
            }
        }
        
        // Log game end analytics
        FirebaseAnalytics.LogEvent("game_ended", 
            new Parameter("result", result),
            new Parameter("moves", GameManager.Instance.LatestHalfMoveIndex));
    }
    
    public void UpdateStats(bool whiteWon)
    {
        // Increment total matches
        totalMatches++;
        
        // Update wins
        if (whiteWon)
            whiteWins++;
        else
            blackWins++;
        
        // Prepare stats for database
        Dictionary<string, object> stats = new Dictionary<string, object>
        {
            { "total_matches", totalMatches },
            { "white_wins", whiteWins },
            { "black_wins", blackWins }
        };
        
        // Update database
        if (databaseRef != null)
        {
            databaseRef.Child("stats").UpdateChildrenAsync(stats).ContinueWith(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError("Failed to update stats: " + task.Exception);
                }
                else
                {
                    // Update UI on main thread
                    UnityMainThreadDispatcher.Instance().Enqueue(UpdateStatsUI);
                }
            });
        }
        else 
        {
            Debug.LogWarning("Database reference is null. Cannot update stats.");
        }
    }
    
    private void LoadData()
    {
        if (isInitialized && databaseRef != null)
        {
            databaseRef.Child("stats").GetValueAsync().ContinueWith(task => {
                if (!task.IsFaulted && task.IsCompleted)
                {
                    DataSnapshot snapshot = task.Result;
                    if (snapshot.Exists)
                    {
                        // Extract values safely
                        totalMatches = SafeExtractInt(snapshot, "total_matches");
                        whiteWins = SafeExtractInt(snapshot, "white_wins");
                        blackWins = SafeExtractInt(snapshot, "black_wins");
                        
                        // Update UI on main thread
                        UnityMainThreadDispatcher.Instance().Enqueue(UpdateStatsUI);
                    }
                }
            });
            
            // Load saved games
            LoadSavedGames();
        }
    }

    private int SafeExtractInt(DataSnapshot snapshot, string key)
    {
        try 
        {
            return snapshot.Child(key).Value != null 
                ? Convert.ToInt32(snapshot.Child(key).Value) 
                : 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error extracting {key}: {ex.Message}");
            return 0;
        }
    }
    
    private void UpdateStatsUI()
    {
        try 
        {
            if (matchesPlayedText != null)
                matchesPlayedText.text = "Total Matches: " + totalMatches;
            
            if (whiteWinsText != null)
                whiteWinsText.text = "White Wins: " + whiteWins;
            
            if (blackWinsText != null)
                blackWinsText.text = "Black Wins: " + blackWins;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error updating stats UI: {ex.Message}");
        }
    }
    
    public void SaveGame()
    {
        if (!isInitialized)
        {
            SetStatus("Firebase not initialized, cannot save game");
            return;
        }
        
        // Get save name
        string saveName = "AutoSave_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        if (saveNameInput != null && !string.IsNullOrEmpty(saveNameInput.text))
        {
            saveName = saveNameInput.text;
        }
        
        // Get game state
        string gameState = GameManager.Instance.SerializeGame();
        
        // Create save data
        Dictionary<string, object> saveData = new Dictionary<string, object>
        {
            { "name", saveName },
            { "timestamp", DateTime.UtcNow.ToString("o") },
            { "game_state", gameState },
            { "move_index", GameManager.Instance.LatestHalfMoveIndex }
        };
        
        // Save to database
        SetStatus("Saving game...");
        databaseRef.Child("saved_games").Child(saveName).SetValueAsync(saveData)
            .ContinueWith(task => {
                if (task.IsFaulted)
                {
                    UnityEngine.Debug.LogError("Error saving game: " + task.Exception);
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        SetStatus("Error saving game: " + task.Exception.Message);
                    });
                }
                else
                {
                    UnityEngine.Debug.Log("Game saved successfully");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        SetStatus("Game saved successfully: " + saveName);
                        
                        // Clear input field
                        if (saveNameInput != null)
                            saveNameInput.text = "";
                        
                        // Reload saved games
                        LoadSavedGames();
                    });
                }
            });
    }
    
    public void LoadGame()
    {
        if (!isInitialized)
        {
            SetStatus("Firebase not initialized, cannot load game");
            return;
        }
        
        if (loadGameDropdown == null || loadGameDropdown.options.Count == 0)
        {
            SetStatus("No saved games available");
            return;
        }
        
        // Get selected save
        string saveName = loadGameDropdown.options[loadGameDropdown.value].text;
        
        // Check if we have this save in our dictionary
        if (savedGames.TryGetValue(saveName, out string gameState))
        {
            // Load the game
            SetStatus("Loading game: " + saveName);
            GameManager.Instance.LoadGame(gameState);
            
            // Update board visuals
            if (BoardManager.Instance != null)
            {
                BoardManager.Instance.OnGameResetToHalfMove();
            }
            
            // Close panel
            if (analyticsPanel != null)
                analyticsPanel.SetActive(false);
        }
        else
        {
            SetStatus("Could not find game state for: " + saveName);
        }
    }
    
    private void LoadSavedGames()
    {
        if (!isInitialized)
        {
            SetStatus("Firebase not initialized, cannot load saved games");
            return;
        }
        
        // Clear saved games
        savedGames.Clear();
        
        // Update dropdown
        if (loadGameDropdown != null)
        {
            loadGameDropdown.ClearOptions();
        }
        
        // Load from database
        databaseRef.Child("saved_games").GetValueAsync().ContinueWith(task => {
            if (task.IsFaulted)
            {
                UnityEngine.Debug.LogError("Error loading saved games: " + task.Exception);
            }
            else if (task.IsCompleted)
            {
                DataSnapshot snapshot = task.Result;
                
                // Process saved games
                List<string> options = new List<string>();
                if (snapshot.Exists)
                {
                    foreach (DataSnapshot save in snapshot.Children)
                    {
                        string name = save.Key;
                        string gameState = save.Child("game_state").Value?.ToString();
                        
                        if (!string.IsNullOrEmpty(gameState))
                        {
                            options.Add(name);
                            savedGames[name] = gameState;
                        }
                    }
                }
                
                // Update UI
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    if (loadGameDropdown != null)
                    {
                        loadGameDropdown.ClearOptions();
                        loadGameDropdown.AddOptions(options);
                        
                        if (options.Count > 0)
                        {
                            SetStatus("Loaded " + options.Count + " saved games");
                        }
                        else
                        {
                            SetStatus("No saved games found");
                        }
                    }
                });
            }
        });
    }
    
    public void ToggleAnalyticsPanel()
    {
        if (analyticsPanel != null)
        {
            analyticsPanel.SetActive(!analyticsPanel.activeSelf);
            
            // If opening panel, update UI
            if (analyticsPanel.activeSelf)
            {
                LoadData();
                UpdateStatsUI();
            }
        }
    }
    public void RecordSkinPurchase(string playerId, string skinId)
    {
        // Add more robust logging
        Debug.Log($"Attempting to record skin purchase - PlayerId: {playerId}, SkinId: {skinId}");
        Debug.Log($"Firebase Initialized: {isInitialized}, Database Reference: {databaseRef != null}");

        if (!isInitialized)
        {
            Debug.LogWarning("Firebase not initialized. Cannot record skin purchase.");
            return;
        }

        if (databaseRef == null)
        {
            Debug.LogWarning("Database reference is null. Cannot record skin purchase.");
            return;
        }

        // Create purchase data with more detailed information
        Dictionary<string, object> purchaseData = new Dictionary<string, object>
        {
            { "player_id", playerId ?? "Unknown" },
            { "skin_id", skinId ?? "Unknown" },
            { "timestamp", DateTime.UtcNow.ToString("o") },
            { "network_role", NetworkManager.Singleton.IsHost ? "Host" : "Client" }
        };

        // Generate a unique key for the purchase
        string purchaseKey = databaseRef.Child("purchases").Push().Key;

        // Save to database with comprehensive error handling
        databaseRef.Child("purchases").Child(purchaseKey).SetValueAsync(purchaseData)
            .ContinueWith(task => {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Failed to record skin purchase: {task.Exception}");
                
                    // Log more detailed exception information
                    if (task.Exception != null)
                    {
                        foreach (var inner in task.Exception.InnerExceptions)
                        {
                            Debug.LogError($"Inner Exception: {inner.Message}");
                        }
                    }
                }
                else
                {
                    Debug.Log($"Skin purchase recorded successfully: {skinId} for player {playerId}");
                }   
            });
    }
    
    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        UnityEngine.Debug.Log("Firebase Status: " + message);
    }
}