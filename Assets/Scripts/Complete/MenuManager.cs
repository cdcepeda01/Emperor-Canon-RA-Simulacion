using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Complete
{
    public class MenuManager : MonoBehaviour
    {
        public static MenuManager Instance { get; private set; }

        public bool menuIsShowing = true;
        public CanvasGroup canvasGroup;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
            canvasGroup = GetComponent<CanvasGroup>();
        }

        public void LoadLevel(string levelName)
        {
            SceneManager.LoadScene(levelName);
            gameObject.SetActive(false);
        }

        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        }

        public void ToggleMenu()
        {
            if (menuIsShowing)
            {
                menuIsShowing = false;
                canvasGroup.alpha = 0;
                Time.timeScale = 1;
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                return;
            }

            menuIsShowing = true;
            canvasGroup.alpha = 1;
            Time.timeScale = 0;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }
    }
}