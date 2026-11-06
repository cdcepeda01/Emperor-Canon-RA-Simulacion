using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
#endif

/// <summary>Transporte C1. La lectura y el cierre del puerto ocurren en un worker.</summary>
[DisallowMultipleComponent]
public class ArduinoSerial : MonoBehaviour
{
    public static ArduinoSerial Instance { get; private set; }
    [Tooltip("COM3, por ejemplo. Vacío: busca un puerto que envíe C1.")]
    public string portName = "";
    [Min(1200)] public int baudRate = 9600;
    [Tooltip("Sin paquetes válidos durante este tiempo se deja de usar Arduino.")]
    [Range(0.2f, 3f)] public float dataTimeout = 0.5f;
    public bool logConnectionChanges = true;
    public bool IsConnected => connected && AgeSeconds(Interlocked.Read(ref lastReceived)) < dataTimeout;
    public string ConnectedPort => IsConnected ? connectedPort : "";
    public string LastError => lastError;
    public int InvalidLines => Volatile.Read(ref invalidLines);

    public sealed class Frame
    {
        public readonly int Vertical, Horizontal, Session;
        public readonly bool Menu, Fire;
        public readonly long ReceivedAt;
        public Frame(int vertical, int horizontal, bool menu, bool fire, int session, long receivedAt)
        {
            Vertical = vertical; Horizontal = horizontal; Menu = menu; Fire = fire;
            Session = session; ReceivedAt = receivedAt;
        }
    }

    const int QueueCapacity = 64;
    readonly object gate = new object();
    readonly Queue<Frame> frames = new Queue<Frame>();
    CancellationTokenSource cancellation;
    volatile bool connected;
    volatile string connectedPort = "";
    volatile string lastError = "";
    long lastReceived;
    int generation, session, invalidLines;
    bool reportedConnected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

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
    }

    void OnEnable()
    {
        if (Instance != null && Instance != this) return;
        Instance = this;
        int workerGeneration = Interlocked.Increment(ref generation);
        connected = false;
        reportedConnected = false;
        lock (gate) frames.Clear();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // Capturar configuración aquí; no acceder a APIs de Unity desde el worker.
        string configuredPort = (portName ?? "").Trim();
        int configuredBaud = Math.Max(1200, baudRate);
        double timeout = Math.Max(0.2, dataTimeout);
        var source = new CancellationTokenSource();
        cancellation = source;
        new Thread(() => ReadLoop(configuredPort, configuredBaud, timeout, workerGeneration, source))
        { IsBackground = true, Name = "Cannon Arduino serial" }.Start();
#else
        lastError = "Arduino serial requiere Windows Editor o Windows Standalone. El teclado sigue activo.";
#endif
    }

    void Update()
    {
        bool state = IsConnected;
        if (state == reportedConnected) return;
        reportedConnected = state;
        if (logConnectionChanges)
            UnityEngine.Debug.Log(state ? $"[Arduino] Conectado en {ConnectedPort}" : "[Arduino] Sin datos; teclado disponible.", this);
    }

    void OnDisable()
    {
        Interlocked.Increment(ref generation);
        cancellation?.Cancel();
        cancellation = null;
        connected = false;
        connectedPort = "";
        lock (gate) frames.Clear();
        // Sin Join ni SerialPort.Close en Unity: el worker observa Cancel en <=100 ms.
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static double AgeSeconds(long timestamp) => (Stopwatch.GetTimestamp() - timestamp) / (double)Stopwatch.Frequency;

    public bool TryDequeue(out Frame frame)
    {
        lock (gate)
        {
            while (frames.Count > 0)
            {
                frame = frames.Dequeue();
                if (IsConnected && AgeSeconds(frame.ReceivedAt) < dataTimeout) return true;
            }
        }
        frame = null;
        return false;
    }

    /// <summary>C1,vertical(0..1023),horizontal(0..1023),menu(0/1),fire(0/1)</summary>
    public static bool TryParseLine(string line, out int vertical, out int horizontal, out bool menu, out bool fire)
    {
        vertical = horizontal = 0;
        menu = fire = false;
        if (string.IsNullOrEmpty(line) || line.Length > 48) return false;
        string[] parts = line.TrimEnd('\r', '\n').Split(',');
        if (parts.Length != 5 || parts[0] != "C1") return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out vertical) || vertical < 0 || vertical > 1023 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out horizontal) || horizontal < 0 || horizontal > 1023 ||
            (parts[3] != "0" && parts[3] != "1") || (parts[4] != "0" && parts[4] != "1")) return false;
        menu = parts[3] == "1";
        fire = parts[4] == "1";
        return true;
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    void ReadLoop(string configuredPort, int configuredBaud, double timeout, int workerGeneration, CancellationTokenSource source)
    {
        CancellationToken token = source.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string[] ports = configuredPort.Length > 0 ? new[] { configuredPort } : SerialPort.GetPortNames();
                    foreach (string port in ports)
                    {
                        if (token.IsCancellationRequested) return;
                        ReadPort(port, configuredBaud, timeout, workerGeneration, token);
                    }
                }
                catch (Exception ex)
                {
                    if (workerGeneration == Volatile.Read(ref generation)) lastError = ex.Message;
                }
                if (token.WaitHandle.WaitOne(1000)) return;
            }
        }
        finally { source.Dispose(); }
    }

    void ReadPort(string portNameToOpen, int configuredBaud, double timeout, int workerGeneration, CancellationToken token)
    {
        try
        {
            using (var port = new SerialPort(portNameToOpen, configuredBaud)
            { ReadTimeout = 100, DtrEnable = true, Encoding = Encoding.ASCII })
            {
                port.Open();
                int currentSession = Interlocked.Increment(ref session);
                long lastValid = Stopwatch.GetTimestamp(), partialStarted = lastValid;
                bool receivedValid = false, discardLine = false;
                var line = new StringBuilder(48);
                while (!token.IsCancellationRequested)
                {
                    // El Arduino puede reiniciarse al abrir COM.
                    if (AgeSeconds(lastValid) > (receivedValid ? timeout : 3.0)) break;
                    try
                    {
                        int next = port.ReadByte();
                        if (next == '\n')
                        {
                            if (!discardLine && TryParseLine(line.ToString(), out int v, out int h, out bool m, out bool f))
                            {
                                lastValid = Stopwatch.GetTimestamp();
                                receivedValid = true;
                                lock (gate)
                                {
                                    if (workerGeneration != Volatile.Read(ref generation) || token.IsCancellationRequested) return;
                                    // Una pausa larga del editor no reproduce botones atrasados.
                                    if (frames.Count >= QueueCapacity)
                                    {
                                        frames.Clear();
                                        currentSession = Interlocked.Increment(ref session);
                                    }
                                    frames.Enqueue(new Frame(v, h, m, f, currentSession, lastValid));
                                    Interlocked.Exchange(ref lastReceived, lastValid);
                                    connectedPort = portNameToOpen;
                                    lastError = "";
                                    connected = true;
                                }
                            }
                            else Interlocked.Increment(ref invalidLines);
                            line.Clear();
                            discardLine = false;
                        }
                        else if (!discardLine)
                        {
                            if (line.Length == 0) partialStarted = Stopwatch.GetTimestamp();
                            if (next > 127 || line.Length >= 48) { line.Clear(); discardLine = true; }
                            else line.Append((char)next);
                        }
                    }
                    catch (TimeoutException)
                    {
                        if (line.Length > 0 && AgeSeconds(partialStarted) > timeout)
                        { line.Clear(); discardLine = true; }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (workerGeneration == Volatile.Read(ref generation)) lastError = ex.Message;
        }
        finally
        {
            lock (gate)
            {
                if (workerGeneration == Volatile.Read(ref generation))
                { connected = false; connectedPort = ""; frames.Clear(); }
            }
        }
    }
#endif
}
