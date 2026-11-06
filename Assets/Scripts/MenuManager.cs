using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>El mismo menú y CanvasGroup para teclado, ratón y Arduino.</summary>
public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance { get; private set; }
    public static int LastMenuInteractionFrame { get; private set; } = -1;
    public bool isMainMenuShowing = true;
    public CanvasGroup canvasGroup;
    SimpleControls controls;

    public static bool IsAnyMenuOpen => ActiveMenuRoot != null;
    public static GameObject ActiveMenuRoot
    {
        get
        {
            PauseMenu pause = FindAnyObjectByType<PauseMenu>();
            if (pause != null && pause.IsOpen)
                return pause.optionsPanel.activeSelf ? pause.optionsPanel : pause.pausePanel;
            if (Instance != null && Instance.isActiveAndEnabled && Instance.isMainMenuShowing)
                return Instance.gameObject;
            MainMenu main = FindAnyObjectByType<MainMenu>();
            if (main != null)
                return main.optionsPanel.activeSelf ? main.optionsPanel : main.mainPanel;
            return null;
        }
    }

    public static void MarkMenuInteraction() => LastMenuInteractionFrame = Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; LastMenuInteractionFrame = -1; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        controls = new SimpleControls();
        controls.gameplay.menu.performed += _ =>
        {
            // PauseMenu ya escucha Esc/Start en las escenas del vehículo.
            if (FindAnyObjectByType<PauseMenu>() == null) ToggleMenu();
        };
    }

    void OnEnable()
    {
        if (Instance == null) Instance = this;
        controls?.gameplay.menu.Enable();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start() => SetVisible(isMainMenuShowing);
    void OnDisable()
    {
        controls?.Disable();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    void OnDestroy()
    {
        controls?.Dispose();
        if (Instance == this) Instance = null;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => MarkMenuInteraction();

    void LateUpdate()
    {
        if (!isMainMenuShowing) return;
        EnsureSelection();
    }

    public void EnsureSelection()
    {
        if (EventSystem.current == null) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(transform) &&
            selected.TryGetComponent(out Selectable existing) && existing.IsActive() && existing.IsInteractable()) return;
        foreach (Selectable choice in GetComponentsInChildren<Selectable>())
        {
            if (!choice.IsActive() || !choice.IsInteractable()) continue;
            EventSystem.current.SetSelectedGameObject(choice.gameObject);
            break;
        }
    }

    public void ToggleMenu()
    {
        // La pantalla principal no tiene una partida a la que regresar.
        if (isMainMenuShowing && SceneManager.GetActiveScene().name == "MainMenu") return;
        SetVisible(!isMainMenuShowing);
    }

    void SetVisible(bool visible)
    {
        MarkMenuInteraction();
        isMainMenuShowing = visible;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
        Time.timeScale = visible ? 0f : 1f;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        if (visible) EnsureSelection();
        else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void LoadLevel(int sceneIndex)
    {
        if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning($"La escena {sceneIndex} no está en Build Profiles.", this);
            return;
        }
        SetVisible(false);
        SceneManager.LoadScene(sceneIndex);
    }

    public void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
