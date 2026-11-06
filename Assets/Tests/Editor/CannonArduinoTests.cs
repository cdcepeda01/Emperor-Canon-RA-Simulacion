using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[Category("CannonArduino")]
public class CannonArduinoTests
{
    [TestCase("C1,0,1023,0,1", true)]
    [TestCase("C1,512,256,1,0\r\n", true)]
    [TestCase("512,512,0,0", false)]
    [TestCase("512,512,0,0,0", false)]
    [TestCase("C1,1024,0,0,0", false)]
    [TestCase("C1,-1,0,0,0", false)]
    [TestCase("C1,512,0,2,0", false)]
    [TestCase("C1,512,0,0", false)]
    [TestCase("C1,512,0,0,1,extra", false)]
    [TestCase("C1,NaN,0,0,0", false)]
    [TestCase("C1, 512,0,0,0", false)]
    [TestCase("", false)]
    public void ProtocolRejectsInvalidOrIncompleteData(string line, bool expected)
    {
        Assert.That(ArduinoSerial.TryParseLine(line, out _, out _, out _, out _), Is.EqualTo(expected));
    }

    [Test]
    public void NoiseAndSingleSpikesDoNotTakeControl()
    {
        var axis = new ArduinoController.Axis();
        axis.Sample(512);
        foreach (int reading in new[] { 513, 511, 514, 510, 512, 1023, 512, 511, 512 })
            axis.Sample(reading);
        Assert.That(axis.Revision, Is.Zero);
        Assert.That(axis.value, Is.EqualTo(512f / 1023f).Within(0.001f));
        for (int reading = 513; reading <= 540; reading++) axis.Sample(reading);
        Assert.That(axis.Revision, Is.GreaterThan(0), "Un giro lento también debe tomar el control.");
    }

    [Test]
    public void CalibrationInversionAndSensitivityClampTheRange()
    {
        var axis = new ArduinoController.Axis { min = 100, max = 900, invert = true, sensitivity = 1.5f };
        axis.Sample(0);
        Assert.That(axis.value, Is.EqualTo(1));
        axis.Sample(1023); axis.Sample(1023);
        Assert.That(axis.value, Is.EqualTo(0));
    }

    [Test]
    public void LastSignificantInputWinsPerAxisAndDisconnectReturnsKeyboard()
    {
        var axis = new ArduinoController.Axis();
        var owner = new CannonController.AxisOwner();
        var otherOwner = new CannonController.AxisOwner();
        axis.Sample(512);
        float current = owner.Resolve(0, 1, 10, -30, 30, true, axis, 0.15f, 90, 0.02f);
        Assert.That(owner.UsingArduino, Is.False);
        Assert.That(current, Is.GreaterThan(0));
        axis.Sample(900); axis.Sample(900);
        current = owner.Resolve(current, 1, 10, -30, 30, true, axis, 0.15f, 90, 0.02f);
        Assert.That(owner.UsingArduino, Is.True, "Un giro posterior gana a una tecla que seguía mantenida.");
        Assert.That(otherOwner.UsingArduino, Is.False, "El otro eje conserva su dispositivo.");
        owner.Resolve(current, 0, 10, -30, 30, true, axis, 0.15f, 90, 0.02f);
        owner.Resolve(current, -1, 10, -30, 30, true, axis, 0.15f, 90, 0.02f);
        Assert.That(owner.UsingArduino, Is.False);
        owner.Resolve(current, 1, 10, -30, 30, false, null, 0.15f, 90, 0.02f);
        Assert.That(owner.UsingArduino, Is.False);
    }

    [Test]
    public void SmoothingIsBoundedAndMenuMovementDoesNotLeakIntoGameplay()
    {
        var axis = new ArduinoController.Axis();
        var owner = new CannonController.AxisOwner();
        axis.Sample(0); axis.Sample(1023); axis.Sample(1023);
        float current = 0;
        for (int i = 0; i < 300; i++)
        {
            float next = owner.Resolve(current, 0, 10, -30, 30, true, axis, 0.15f, 40, 0.02f);
            Assert.That(next, Is.InRange(-30f, 30f));
            Assert.That(Mathf.Abs(next - current), Is.LessThanOrEqualTo(40 * 0.02f + 0.01f));
            current = next;
        }
        Assert.That(current, Is.EqualTo(30).Within(0.01));
        owner.Suspend(0, axis.Revision);
        Assert.That(owner.Resolve(5, 0, 10, -30, 30, true, axis, 0.15f, 40, 0.02f), Is.EqualTo(5));
    }

    // Emula exclusivamente la llegada de bytes ya interpretados. Controller.Update,
    // filtros, Input System, transformaciones, UI, recarga y Fire son los reales.
    static void Inject(ArduinoSerial serial, int v, int h, bool menu, bool fire, int session = 70)
    {
        long now = Stopwatch.GetTimestamp();
        Set(serial, "connected", true);
        Set(serial, "lastReceived", now);
        ((Queue<ArduinoSerial.Frame>)Get(serial, "frames")).Enqueue(new ArduinoSerial.Frame(v, h, menu, fire, session, now));
    }

    static IEnumerator Pump(ArduinoSerial serial, float seconds, int v = 512, int h = 512,
        bool menu = false, bool fire = false, int session = 70)
    {
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        do { Inject(serial, v, h, menu, fire, session); yield return null; }
        while (Time.realtimeSinceStartupAsDouble < end);
    }

    static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    static int Shots() => Object.FindObjectsByType<Rigidbody>().Count(b => b.name == "ArduinoTestProjectile(Clone)");

    [UnityTest]
    public IEnumerator RealSceneSupportsKeyboardArduinoMenusAndReconnect()
    {
        yield return new EnterPlayMode();
        SceneManager.LoadScene("Level_01");
        yield return null;
        var cannon = Object.FindAnyObjectByType<CannonController>();
        var serial = ArduinoSerial.Instance;
        var adapter = ArduinoController.Instance;
        var menu = MenuManager.Instance;
        Assert.That(cannon, Is.Not.Null);
        Assert.That(serial, Is.Not.Null);
        Assert.That(adapter, Is.Not.Null);
        Assert.That(menu, Is.Not.Null);
        Assert.That(menu.isMainMenuShowing, Is.False);

        // Detener COM para que esta prueba nunca dependa de una placa o puerto.
        serial.enabled = false;
        yield return new WaitForSecondsRealtime(0.2f);
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var probe = new GameObject("ArduinoTestProjectile");
        probe.transform.position = Vector3.one * 10000;
        probe.AddComponent<Rigidbody>().useGravity = false;
        GameObject originalProjectile = cannon.projectilePrefab;
        float originalDelay = cannon.shotDelay;
        GameObject confirm = null;
        try
        {
            yield return Pump(serial, 0.08f);
            float initialAim = cannon.VerticalAngle;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return Pump(serial, 0.15f);
            Assert.That(cannon.VerticalAngle, Is.LessThan(initialAim), "W conserva su dirección original.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Pump(serial, 0.05f);
            float initialTurn = cannon.HorizontalAngle;
            yield return Pump(serial, 0.5f, 0, 512);
            Assert.That(cannon.VerticalAngle, Is.GreaterThan(initialAim));
            Assert.That(cannon.HorizontalAngle, Is.EqualTo(initialTurn).Within(0.01f));
            Assert.That(cannon.ArduinoOwnsHorizontal, Is.False);
            yield return Pump(serial, 0.8f, 0, 512);
            float settledAim = cannon.VerticalAngle;
            yield return Pump(serial, 0.5f, 0, 1023);
            Assert.That(cannon.HorizontalAngle, Is.GreaterThan(initialTurn));
            Assert.That(cannon.VerticalAngle, Is.EqualTo(settledAim).Within(0.1f));
            Assert.That(cannon.VerticalAngle, Is.InRange(cannon.aimRotationMin, cannon.aimRotationMax));
            Assert.That(cannon.HorizontalAngle, Is.InRange(cannon.turnRotationMin, cannon.turnRotationMax));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
            float beforeKeyboard = cannon.HorizontalAngle;
            yield return Pump(serial, 0.15f, 0, 1023);
            Assert.That(cannon.HorizontalAngle, Is.LessThan(beforeKeyboard), "A toma el control con el potenciómetro quieto.");
            Assert.That(cannon.ArduinoOwnsHorizontal, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Pump(serial, 0.05f, 0, 1023);

            cannon.projectilePrefab = probe;
            cannon.shotDelay = 0.4f;
            Set(cannon, "timeSinceLastShot", 10f);
            int shots = Shots();
            yield return Pump(serial, 0.08f, 0, 1023, fire: true);
            Assert.That(Shots(), Is.EqualTo(shots + 1));
            yield return Pump(serial, 0.08f, 0, 1023);
            yield return Pump(serial, 0.08f, 0, 1023, fire: true);
            Assert.That(Shots(), Is.EqualTo(shots + 1), "Se respeta la recarga.");
            yield return Pump(serial, 0.5f, 0, 1023, fire: true);
            Assert.That(Shots(), Is.EqualTo(shots + 1), "Mantener el botón no repite disparos.");
            yield return Pump(serial, 0.08f, 0, 1023);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return Pump(serial, 0.05f, 0, 1023);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Pump(serial, 0.08f, 0, 1023);
            Assert.That(Shots(), Is.EqualTo(shots + 2), "Space usa la misma acción de disparo.");

            yield return Pump(serial, 0.08f, 0, 1023, menu: true);
            Assert.That(menu.isMainMenuShowing, Is.True);
            yield return Pump(serial, 0.15f, 0, 1023, menu: true);
            Assert.That(menu.isMainMenuShowing, Is.True, "Menú mantenido no alterna continuamente.");
            yield return Pump(serial, 0.08f, 0, 1023);
            confirm = new GameObject("TestConfirm", typeof(RectTransform), typeof(Button));
            confirm.transform.SetParent(menu.transform, false);
            int confirmations = 0;
            confirm.GetComponent<Button>().onClick.AddListener(() => { confirmations++; menu.ToggleMenu(); });
            EventSystem.current.SetSelectedGameObject(confirm);
            shots = Shots();
            yield return Pump(serial, 0.08f, 0, 1023, fire: true);
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(menu.isMainMenuShowing, Is.False);
            yield return Pump(serial, 0.5f, 0, 1023, fire: true);
            Assert.That(Shots(), Is.EqualTo(shots), "Seleccionar y mantener al cerrar no dispara.");
            yield return Pump(serial, 0.08f, 0, 1023);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return Pump(serial, 0.06f, 0, 1023);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Pump(serial, 0.06f, 0, 1023);
            Assert.That(menu.isMainMenuShowing, Is.True, "Esc abre el mismo menú.");
            EventSystem.current.SetSelectedGameObject(confirm);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            yield return Pump(serial, 0.06f, 0, 1023);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Pump(serial, 0.1f, 0, 1023);
            Assert.That(confirmations, Is.EqualTo(2), "Enter conserva la confirmación UI.");
            Assert.That(Shots(), Is.EqualTo(shots), "Confirmación de teclado tampoco dispara.");

            // Dos botones simultáneos: únicamente abrir menú.
            yield return Pump(serial, 0.08f, 0, 1023, menu: true, fire: true);
            Assert.That(menu.isMainMenuShowing, Is.True);
            Assert.That(confirmations, Is.EqualTo(2));
            Assert.That(Shots(), Is.EqualTo(shots));
            yield return Pump(serial, 0.08f, 0, 1023);
            yield return Pump(serial, 0.08f, 0, 1023, menu: true);
            Assert.That(menu.isMainMenuShowing, Is.False);
            yield return Pump(serial, 0.08f, 0, 1023);

            Set(serial, "connected", false);
            yield return null;
            Assert.That(adapter.HasInput, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            beforeKeyboard = cannon.HorizontalAngle;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(cannon.HorizontalAngle, Is.GreaterThan(beforeKeyboard), "Desconectar no impide usar D.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return Pump(serial, 0.15f, 512, 512, fire: true, session: 71);
            Assert.That(Shots(), Is.EqualTo(shots), "Reconectar con disparo mantenido no dispara.");
            Assert.That(cannon.ArduinoOwnsVertical, Is.False, "Reconectar no reclama el movimiento.");
            yield return Pump(serial, 0.08f, session: 71);
            yield return Pump(serial, 0.08f, fire: true, session: 71);
            Assert.That(Shots(), Is.EqualTo(shots + 1), "Después de soltar, el botón vuelve a funcionar.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            cannon.projectilePrefab = originalProjectile;
            cannon.shotDelay = originalDelay;
            if (confirm != null) Object.Destroy(confirm);
            Object.Destroy(probe);
            foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>())
                if (body.name == "ArduinoTestProjectile(Clone)") Object.Destroy(body.gameObject);
            Set(serial, "connected", false);
            serial.enabled = true;
            Time.timeScale = 1f;
        }
    }

    [UnityTearDown]
    public IEnumerator LeavePlayMode()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator MainMenuLoadsCannonAndReusesPersistentComponents()
    {
        yield return new EnterPlayMode();
        SceneManager.LoadScene("MainMenu");
        yield return null;
        MenuManager menu = MenuManager.Instance;
        ArduinoSerial serial = ArduinoSerial.Instance;
        ArduinoController adapter = ArduinoController.Instance;
        Assert.That(menu, Is.Not.Null);
        Assert.That(menu.isMainMenuShowing, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("Level 1"));
        Button levelButton = EventSystem.current.currentSelectedGameObject.GetComponent<Button>();
        Assert.That(levelButton.IsInteractable(), Is.True, "El botón Level 1 debe aceptar Submit.");
        Assert.That(levelButton.onClick.GetPersistentEventCount(), Is.EqualTo(1), "Debe conservar el OnClick serializado.");
        Assert.That(levelButton.onClick.GetPersistentMethodName(0), Is.EqualTo("LoadLevel"));
        Assert.That(levelButton.onClick.GetPersistentTarget(0), Is.SameAs(menu));
        Assert.That(levelButton.onClick.GetPersistentListenerState(0), Is.EqualTo(UnityEngine.Events.UnityEventCallState.RuntimeOnly));
        Assert.That(new SerializedObject(levelButton).FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Mode").intValue,
            Is.EqualTo(3), "LoadLevel requiere un argumento int.");
        Assert.That(new SerializedObject(levelButton).FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_IntArgument").intValue,
            Is.EqualTo(2), "El argumento serializado debe apuntar a Level_01.");
        int submits = 0;
        levelButton.onClick.AddListener(() => submits++);
        serial.enabled = false;
        yield return new WaitForSecondsRealtime(0.2f);
        try
        {
            yield return Pump(serial, 0.08f);
            // Confirmar el botón real, con sus referencias serializadas reales.
            yield return Pump(serial, 0.25f, fire: true);
            Assert.That(submits, Is.EqualTo(1), "Submit debe invocar el Button real una vez.");
            Assert.That(menu.isMainMenuShowing, Is.False, "El callback persistente debe cerrar el menú antes de cargar.");
            // LoadScene completa la carga al final del frame; esperar también si
            // el primer frame debe cargar shaders/iluminación del escenario.
            double sceneDeadline = Time.realtimeSinceStartupAsDouble + 5;
            while (SceneManager.GetActiveScene().name != "Level_01" && Time.realtimeSinceStartupAsDouble < sceneDeadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Level_01"));
            Assert.That(ArduinoSerial.Instance, Is.SameAs(serial));
            Assert.That(ArduinoController.Instance, Is.SameAs(adapter));
            Assert.That(MenuManager.Instance, Is.SameAs(menu));
            Assert.That(Object.FindObjectsByType<ArduinoSerial>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ArduinoController>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<MenuManager>().Length, Is.EqualTo(1));
            Assert.That(Object.FindAnyObjectByType<CannonController>(), Is.Not.Null);
            Assert.That(menu.isMainMenuShowing, Is.False);
            yield return Pump(serial, 0.08f);
            yield return Pump(serial, 0.08f, menu: true);
            Assert.That(menu.isMainMenuShowing, Is.True);
            yield return Pump(serial, 0.08f);
            yield return Pump(serial, 0.08f, menu: true);
            Assert.That(menu.isMainMenuShowing, Is.False);
        }
        finally
        {
            Set(serial, "connected", false);
            serial.enabled = true;
            Time.timeScale = 1f;
        }
    }
}

[InitializeOnLoad]
public static class CannonArduinoTestMenu
{
    static readonly TestRunnerApi Api;
    static CannonArduinoTestMenu()
    {
        Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        Api.RegisterCallbacks(new Results());
    }

    [MenuItem("Tools/Arduino/Run integration checks %#F8")]
    public static void Run()
    {
        Api.Execute(new ExecutionSettings(new Filter
        { testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode, categoryNames = new[] { "CannonArduino" } }));
    }

    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            Directory.CreateDirectory("Logs");
            TestRunnerApi.SaveResultToFile(result, "Logs/Arduino-test-results.xml");
            UnityEngine.Debug.Log($"[Arduino checks] Passed: {result.PassCount}; Failed: {result.FailCount}; Skipped: {result.SkipCount}. Logs/Arduino-test-results.xml");
        }
    }
}
