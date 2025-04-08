using UnityEngine;
using UnityEngine.UI;

public class AnalyticsButtonHandler : MonoBehaviour
{
    [SerializeField] private Button openAnalyticsButton;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button loadGameButton;
    [SerializeField] private Button testWhiteWinButton;
    [SerializeField] private Button testBlackWinButton;
    
    private void Start()
    {
        // Setup buttons
        if (openAnalyticsButton != null)
            openAnalyticsButton.onClick.AddListener(OpenAnalyticsPanel);
        
        if (saveGameButton != null)
            saveGameButton.onClick.AddListener(SaveGame);
        
        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(LoadGame);
        
        // Add test win buttons
        if (testWhiteWinButton != null)
            testWhiteWinButton.onClick.AddListener(TestWhiteWin);
        
        if (testBlackWinButton != null)
            testBlackWinButton.onClick.AddListener(TestBlackWin);
    }
    
    public void OpenAnalyticsPanel()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.ToggleAnalyticsPanel();
        }
        else
        {
            Debug.LogWarning("FirebaseAnalyticsManager not found in scene!");
        }
    }
    
    public void SaveGame()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.SaveGame();
        }
    }
    
    public void LoadGame()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.LoadGame();
        }
    }
    
    public void TestWhiteWin()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.UpdateStats(true); // true for white win
            Debug.Log("White Win Tested and Logged");
        }
    }

    public void TestBlackWin()
    {
        if (FirebaseAnalyticsManager.Instance != null)
        {
            FirebaseAnalyticsManager.Instance.UpdateStats(false); // false for black win
            Debug.Log("Black Win Tested and Logged");
        }
    }
}