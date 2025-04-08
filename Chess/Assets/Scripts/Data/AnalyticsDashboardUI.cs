using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A comprehensive UI controller for the analytics dashboard.
/// </summary>
public class AnalyticsDashboardUI : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject analyticsPanelRoot;
    [SerializeField] private GameObject gameStatesPanelRoot;

    [Header("Analytics Elements")]
    [SerializeField] private TextMeshProUGUI matchesPlayedText;
    [SerializeField] private TextMeshProUGUI matchesWonWhiteText;
    [SerializeField] private TextMeshProUGUI matchesWonBlackText;
    [SerializeField] private TextMeshProUGUI topDLCPurchasedText;
    [SerializeField] private TextMeshProUGUI averageGameLengthText;
    
    [Header("Game State Elements")]
    [SerializeField] private TMP_InputField saveGameNameInput;
    [SerializeField] private TMP_Dropdown savedGamesDropdown;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button loadGameButton;
    
    [Header("Navigation Buttons")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button switchTabButton;
    
    [Header("Test Win Buttons")]
    [SerializeField] private Button testWhiteWinButton;
    [SerializeField] private Button testBlackWinButton;
    
    private bool isShowingAnalytics = true;

    private void Start()
    {
        // Setup all button listeners
        SetupButtonListeners();
        
        // Initialize panel states
        InitializePanels();
        
        // Connect to Firebase manager
        ConnectToFirebaseManager();
    }

    private void SetupButtonListeners()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
        
        if (switchTabButton != null)
            switchTabButton.onClick.AddListener(SwitchTab);
        
        if (saveGameButton != null)
            saveGameButton.onClick.AddListener(SaveGame);
        
        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(LoadGame);
        
        // Add test win button listeners if they exist
        if (testWhiteWinButton != null)
            testWhiteWinButton.onClick.AddListener(TestWhiteWin);
        
        if (testBlackWinButton != null)
            testBlackWinButton.onClick.AddListener(TestBlackWin);
    }

    private void InitializePanels()
    {
        if (analyticsPanelRoot != null)
            analyticsPanelRoot.SetActive(true);
        
        if (gameStatesPanelRoot != null)
            gameStatesPanelRoot.SetActive(false);
    }

    private void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    private void SwitchTab()
    {
        isShowingAnalytics = !isShowingAnalytics;
        
        if (analyticsPanelRoot != null)
            analyticsPanelRoot.SetActive(isShowingAnalytics);
        
        if (gameStatesPanelRoot != null)
            gameStatesPanelRoot.SetActive(!isShowingAnalytics);
        
        // Update button text
        UpdateSwitchTabButtonText();
    }

    private void UpdateSwitchTabButtonText()
    {
        if (switchTabButton != null && switchTabButton.GetComponentInChildren<TextMeshProUGUI>() != null)
        {
            switchTabButton.GetComponentInChildren<TextMeshProUGUI>().text = 
                isShowingAnalytics ? "Saved Games" : "Analytics";
        }
    }

    private void SaveGame()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.SaveGame();
        }
    }

    private void LoadGame()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.LoadGame();
        }
    }

    private void TestWhiteWin()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.UpdateStats(true);
            Debug.Log("White Win Tested and Logged");
        }
    }

    private void TestBlackWin()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.UpdateStats(false);
            Debug.Log("Black Win Tested and Logged");
        }
    }

    private void ConnectToFirebaseManager()
    {
        FirebaseAnalyticsManager firebaseManager = FindObjectOfType<FirebaseAnalyticsManager>();
        
        if (firebaseManager != null)
        {
            // Dynamically set UI references using reflection
            System.Reflection.FieldInfo[] fields = typeof(FirebaseAnalyticsManager).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            foreach (var field in fields)
            {
                // Match and set UI references
                if (field.FieldType == typeof(TextMeshProUGUI))
                {
                    if (field.Name == "matchesPlayedText" && matchesPlayedText != null)
                        field.SetValue(firebaseManager, matchesPlayedText);
                    else if (field.Name == "whiteWinsText" && matchesWonWhiteText != null)
                        field.SetValue(firebaseManager, matchesWonWhiteText);
                    else if (field.Name == "blackWinsText" && matchesWonBlackText != null)
                        field.SetValue(firebaseManager, matchesWonBlackText);
                }
                else if (field.FieldType == typeof(TMP_InputField) && field.Name == "saveNameInput" && saveGameNameInput != null)
                {
                    field.SetValue(firebaseManager, saveGameNameInput);
                }
                else if (field.FieldType == typeof(TMP_Dropdown) && field.Name == "loadGameDropdown" && savedGamesDropdown != null)
                {
                    field.SetValue(firebaseManager, savedGamesDropdown);
                }
            }
        }
        else
        {
            Debug.LogWarning("FirebaseAnalyticsManager not found in the scene!");
        }
    }
}