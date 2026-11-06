using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Adapta C1 a las acciones del cañón y al EventSystem existente.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class ArduinoController : MonoBehaviour
{
    public static ArduinoController Instance { get; private set; }

    [Serializable]
    public class Axis
    {
        public int min = 0, max = 1023;
        public bool invert;
        [Range(0.1f, 2f)] public float sensitivity = 1f;
        [Tooltip("Histéresis contra ruido, en unidades del ADC.")]
        [Range(1, 30)] public int noiseThreshold = 4;
        [Tooltip("Movimiento acumulado mínimo para tomar el control al teclado.")]
        [Range(2, 100)] public int activityThreshold = 12;
        [Header("Diagnóstico en Play")]
        public int raw;
        public float value;
        public int Revision { get; private set; }
        bool initialized;
        int a, b, accepted, activityAnchor;

        public void Reset() { initialized = false; Revision = 0; }

        public bool Sample(int reading)
        {
            raw = reading;
            if (!initialized)
            {
                a = b = accepted = activityAnchor = reading;
                initialized = true;
                UpdateValue();
                return false; // Conectar la placa no mueve el cañón.
            }
            // Mediana de tres muestras contra picos de una sola lectura.
            int median = Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), reading));
            a = b; b = reading;
            if (Mathf.Abs(median - accepted) >= Mathf.Max(1, noiseThreshold)) accepted = median;
            UpdateValue();
            if (Mathf.Abs(accepted - activityAnchor) < Mathf.Max(noiseThreshold + 1, activityThreshold)) return false;
            activityAnchor = accepted;
            Revision++;
            return true;
        }

        void UpdateValue()
        {
            float t = Mathf.InverseLerp(min, Mathf.Max(min + 1, max), accepted);
            if (invert) t = 1f - t;
            value = Mathf.Clamp01(0.5f + (t - 0.5f) * sensitivity);
        }
    }

    public Axis vertical = new Axis { invert = true };
    public Axis horizontal = new Axis();
    [Tooltip("Vacío: busca el cañón activo al cambiar de escena.")]
    public CannonController cannon;
    [Tooltip("Filtrado adicional en Unity; el sketch también elimina rebotes.")]
    [Range(0f, 0.1f)] public float buttonDebounce = 0.025f;
    [Tooltip("En el menú, el potenciómetro vertical elige la opción resaltada.")]
    public bool navigateMenusWithPotentiometers = true;
    public bool HasInput { get; private set; }
    public int InputSession => currentSession;
    int currentSession = -1;
    readonly DebouncedButton menu = new DebouncedButton();
    readonly DebouncedButton fire = new DebouncedButton();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void OnEnable()
    {
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
        ResetInput();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        ResetInput();
    }

    void ResetInput()
    {
        HasInput = false;
        currentSession = -1;
        vertical.Reset(); horizontal.Reset();
        menu.Reset(false, 0); fire.Reset(false, 0);
    }

    void Update()
    {
        ArduinoSerial serial = ArduinoSerial.Instance;
        if (serial == null || !serial.IsConnected) { if (HasInput) ResetInput(); return; }
        if (cannon == null || !cannon.isActiveAndEnabled) cannon = FindAnyObjectByType<CannonController>();
        while (serial.TryDequeue(out ArduinoSerial.Frame frame)) ProcessFrame(frame);
    }

    // La prueba sin hardware utiliza esta misma ruta, sin reemplazar filtros ni acciones.
    public void ProcessFrame(ArduinoSerial.Frame frame)
    {
        double now = frame.ReceivedAt / (double)System.Diagnostics.Stopwatch.Frequency;
        if (currentSession != frame.Session)
        {
            ResetInput();
            currentSession = frame.Session;
            menu.Reset(frame.Menu, now); fire.Reset(frame.Fire, now);
            vertical.Sample(frame.Vertical); horizontal.Sample(frame.Horizontal);
            HasInput = true;
            return; // Al reconectar, un botón mantenido requiere soltarse antes de actuar.
        }
        bool vChanged = vertical.Sample(frame.Vertical);
        bool hChanged = horizontal.Sample(frame.Horizontal);
        bool menuPressed = menu.Sample(frame.Menu, now, buttonDebounce);
        bool firePressed = fire.Sample(frame.Fire, now, buttonDebounce);
        if (vChanged || hChanged || menuPressed || firePressed) InputHint.UsingArduino = true;
        // Si llegan juntos, menú tiene prioridad y consume la pulsación de disparo.
        if (menuPressed) { ToggleMenu(); return; }
        bool inMenu = MenuManager.IsAnyMenuOpen;
        if (inMenu && navigateMenusWithPotentiometers) NavigateMenu(vChanged, hChanged);
        if (!firePressed) return;
        if (inMenu) Submit();
        else if (cannon != null) cannon.Fire();
    }

    void ToggleMenu()
    {
        MenuManager.MarkMenuInteraction();
        PauseMenu pause = FindAnyObjectByType<PauseMenu>();
        if (pause != null) { pause.OpenOrBack(); return; }
        if (MenuManager.Instance != null) { MenuManager.Instance.ToggleMenu(); return; }
        MainMenu main = FindAnyObjectByType<MainMenu>();
        if (main != null) main.Back();
    }

    static Selectable[] MenuOptions()
    {
        GameObject root = MenuManager.ActiveMenuRoot;
        if (root == null) return new Selectable[0];
        return Array.FindAll(root.GetComponentsInChildren<Selectable>(), s => s.IsActive() && s.IsInteractable());
    }

    void NavigateMenu(bool vChanged, bool hChanged)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return;
        Selectable[] choices = MenuOptions();
        if (choices.Length == 0) return;
        if (vChanged)
        {
            float t = vertical.invert ? 1f - vertical.value : vertical.value;
            int index = Mathf.Min(choices.Length - 1, Mathf.FloorToInt(t * choices.Length));
            events.SetSelectedGameObject(choices[index].gameObject);
        }
        if (hChanged && events.currentSelectedGameObject != null &&
            events.currentSelectedGameObject.TryGetComponent(out Slider slider) && slider.IsInteractable())
            slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, horizontal.value);
    }

    public void Submit()
    {
        if (!MenuManager.IsAnyMenuOpen || EventSystem.current == null) return;
        MenuManager.MarkMenuInteraction();
        Selectable[] choices = MenuOptions();
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null || !Array.Exists(choices, s => s.gameObject == selected))
        {
            selected = choices.Length > 0 ? choices[0].gameObject : null;
            EventSystem.current.SetSelectedGameObject(selected);
        }
        if (selected != null)
            ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
    }

    sealed class DebouncedButton
    {
        bool candidate, stable, armed;
        double changedAt;
        public void Reset(bool down, double now)
        { candidate = stable = down; armed = !down; changedAt = now; }
        public bool Sample(bool down, double now, float debounce)
        {
            if (candidate != down) { candidate = down; changedAt = now; }
            if (candidate == stable || now - changedAt < debounce) return false;
            stable = candidate;
            if (!stable) { armed = true; return false; }
            if (!armed) return false;
            armed = false;
            return true;
        }
    }
}
