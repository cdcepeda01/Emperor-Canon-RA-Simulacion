# Cañón con Arduino y teclado

## Estado y análisis del proyecto

Unity **6000.6.0f1**, Input System **1.20.0**, destino comprobado: **Windows Editor / Windows Standalone**. Standalone ya utiliza API Compatibility Level **.NET Framework**. En otras plataformas la parte serial se excluye por compilación y el teclado permanece disponible; no se afirma compatibilidad USB con Android/WebGL.

Se revisaron Assets, Packages, ProjectSettings, escenas, prefabs, scripts de juego, UI, simulación, ejemplos Complete y Arduino/Arduino.ino antes de editar.

- **Assets/Scripts/CannonController.cs**: cañón activo de Level_01. Movimiento local X del barrel y local Y de la base; ruedas y disparo con Rigidbody, impulso y shotDelay.
- **Assets/Samples/Input System/1.11.2/Simple Demo/SimpleControls**: acciones existentes. W/S y A/D, Space/acción primaria, Esc/Start. Se conservan el asset, sus bindings y las interacciones Tap/SlowTap.
- **Assets/Scripts/MenuManager.cs**: menú utilizado por MainMenu.unity, persistente entre escenas. Sus botones existentes cargan niveles y salen.
- **Assets/Scripts/UI/PauseMenu.cs y MainMenu.cs**: UI de la arquitectura de vehículo; el adaptador la reconoce cuando existe. Esos scripts y sus bindings permanecen intactos.
- **Assets/Scripts/Arduino/ArduinoSerial.cs y ArduinoController.cs**, **Assets/Prefabs/Arduino.prefab**: implementación anterior de acelerador/timón/freno. Se adapta este mismo par de componentes al cañón. El adaptador deja de sobrescribir Drive del vehículo.
- **Assets/Scripts/Complete/**: ejemplos de curso, sin referencias al cañón activo. No se modifican.
- **PlayerController, Pawn, Shot, ExplosiveShot, simulación, materiales, pivotes y físicas**: no se cambian.

Level_01 es la escena que actualmente tiene el cañón jugable. Level_02 y Level_03 son escenarios sin CannonController; añadir compatibilidad de entrada no añade un cañón a esos escenarios.

## Componentes y referencias ya configurados

- El prefab Arduino está en MainMenu y Level_01. Persiste con DontDestroyOnLoad y descarta duplicados.
- Level_01 incluye una instancia del menú existente, inicialmente cerrada, para que Esc y el botón físico funcionen al iniciar directamente esa escena.
- El menú mantiene su EventSystem y selección de Unity UI; abrirlo pausa y bloquea disparos y movimiento. Cerrarlo restaura la partida.
- Un CanvasGroup invisible deja de interceptar clics. Se elige una opción inicial para permitir navegación con teclado.
- Build Profiles conserva MainMenu=0 y Base=1, y añade Level_01=2, Level_02=3, Level_03=4. Los tres botones Level N de MainMenu apuntan a su nivel homónimo. Base permanece en el proyecto y en la compilación.
- Al entrar desde MainMenu, el Arduino y menú persistentes se reutilizan. No arrastrar otra copia dentro del cañón.

## Cableado propuesto (Arduino Uno/Nano clásico)

Dos potenciómetros lineales de aproximadamente 10 kΩ y dos pulsadores normalmente abiertos. Son pines propuestos: todas las constantes están al inicio del sketch.

| Componente | Conexión |
|---|---|
| Potenciómetro vertical, terminal central | A0 |
| Potenciómetro horizontal, terminal central | A1 |
| Terminales exteriores de ambos potenciómetros | VCC y GND |
| Botón de menú | D2 a GND al presionar |
| Botón disparo/confirmar | D3 a GND al presionar |
| Comunicación/alimentación | USB entre Arduino y PC |

```text
VCC ---[ potenciómetro 1 ]--- GND
                 |
                 A0   (vertical)

VCC ---[ potenciómetro 2 ]--- GND
                 |
                 A1   (horizontal)

D2 -----[ botón menú ]------ GND
D3 -----[ botón disparo ]--- GND
PC  ========== USB ======== Arduino
```

Usar 5 V de la propia placa para Uno/Nano de 5 V; en placas de 3,3 V usar su tensión de referencia adecuada. Los pulsadores usan INPUT_PULLUP: sueltos leen HIGH y presionados LOW; no necesitan una resistencia externa de pull-down. En pulsadores de cuatro patas, conectar contactos de lados que se unen al pulsar.

El sketch presupone ADC de **10 bits (0–1023)**. Si se usa una placa con otra resolución, configurarla a 10 bits o adaptar el rango del protocolo/validación.

Referencias: [lectura analógica oficial](https://docs.arduino.cc/tutorials/uno-rev3/AnalogReadSerial), [INPUT_PULLUP](https://docs.arduino.cc/language-reference/en/variables/constants/inputOutputPullup/).

## Comunicación

ASCII CSV con cinco campos y fin de línea LF (CRLF también se acepta):

```text
C1,vertical,horizontal,menu,disparo
C1,512,300,0,1
```

C1 identifica la versión. Los dos valores analógicos deben estar entre 0 y 1023 y los botones ser exactamente 0 o 1. No se aceptan otros números de campos, prefijos, texto ni líneas parciales. El antiguo protocolo del vehículo de cinco números se rechaza deliberadamente: cargar el sketch actualizado.

- **9600 baudios**, 8 bits, sin paridad, 1 bit de parada; ambos extremos deben coincidir.
- Un paquete cada **25 ms (40 Hz)**. Se evitan delays en el sketch.
- Antirrebote de 25 ms en el sketch y filtrado adicional configurable en Unity.
- Unity lee en un hilo de fondo, con timeout de lectura de 100 ms y líneas de longitud limitada.
- Se publica cada paquete completo e inmutable en una cola acotada. El hilo principal consume los paquetes y ejecuta las acciones de Unity.
- Con 0,5 s sin datos válidos, deja de usar Arduino. El worker reintenta la conexión; al abrir COM permite hasta 3 s para el reinicio de la placa.
- Paquetes viejos se descartan. Si se llena la cola, se empieza una nueva sesión para no reproducir pulsaciones atrasadas.
- Una sesión nueva toma referencias iniciales sin mover el cañón ni activar botones mantenidos. Estos deben soltarse antes de volver a actuar.
- LastError, InvalidLines, IsConnected y ConnectedPort permiten depurar. Solo se registran cambios de conexión, sin inundar la consola.

## Coexistencia de controles

No hay selector manual de dispositivo y el Input System permanece activo.

| Acción | Teclado/control existente | Arduino |
|---|---|---|
| Inclinación | W/S, stick | Potenciómetro A0 |
| Rotación horizontal | A/D, stick | Potenciómetro A1 |
| Disparo | Space / acción primaria original | D3, nueva pulsación |
| Abrir/cerrar menú en partida | Esc / Start | D2, nueva pulsación |
| Navegar por UI | Flechas/WASD según el módulo UI, ratón/control | Potenciómetro vertical |
| Confirmar UI | Enter / Submit existente, clic | D3 |
| Ajustar slider seleccionado | Controles UI existentes | Potenciómetro horizontal |

El movimiento se arbitra **independientemente por eje**. Un giro significativo del potenciómetro toma su eje; una pulsación posterior del teclado vuelve a tomarlo. Si ambos cambian en el mismo frame, gana el teclado. Una tecla mantenida no genera actividad nueva cada frame: para retomar el control tras un giro posterior, soltar y pulsar de nuevo.

Una mediana de tres muestras rechaza picos; una histéresis de 4 unidades evita vibración y un cambio acumulado de 12 unidades determina actividad significativa. Conectar o dejar quietos los potenciómetros no reclama el control. Un giro lento sí acumula suficiente cambio. SmoothDamp y una velocidad máxima suavizan la llegada a cada ángulo.

El menú usa el mismo EventSystem para ambos dispositivos. El potenciómetro vertical cambia la selección solo cuando se mueve significativamente; así una posición fija no impide navegar con teclado. D3 se convierte en Submit y nunca se dirige a Fire mientras hay un menú abierto. También se bloquea el frame de transición para evitar disparar al confirmar una opción que cierre el menú. Con ambos botones simultáneos tiene prioridad el menú.

## Inspector

En **Assets/Prefabs/Arduino.prefab**:

- ArduinoSerial: Port Name, Baud Rate, Data Timeout, Log Connection Changes.
- ArduinoController: Vertical/Horizontal, Min/Max para calibrar el ADC, Invert, Sensitivity, Noise Threshold, Activity Threshold, Button Debounce y Navigate Menus With Potentiometers.
- Cannon puede quedar vacío: se busca el cañón activo. Raw y Value muestran la lectura y su valor normalizado durante Play.

En el **CannonController de Level_01**:

- Aim Rotation Min/Max: ángulo X local permitido de la pieza barrel; actualmente −30° a +30°.
- Turn Rotation Min/Max: giro Y local relativo a la orientación inicial de la base; actualmente −60° a +60°.
- Aim Speed/Turn Speed: velocidades originales del teclado.
- Arduino Smooth Time: 0,15 s; Arduino Max Speed: 90°/s por defecto.
- Shot Delay y Projectile Force: las restricciones existentes de disparo se comparten entre dispositivos.

La inversión vertical predeterminada hace que un valor mayor eleve el cañón (X local negativo en este modelo). Invert permite ajustarlo al sentido del cableado. No se cambian padres, pivotes ni referencias de barrel, ruedas o punto de salida; se conservan los otros ejes locales.

## Puesta en marcha

1. Abrir Arduino/Arduino.ino en Arduino IDE. Elegir la placa y el puerto correctos, revisar pines y cargar el sketch.
2. Abrir brevemente el Monitor Serial a 9600. Comprobar líneas como C1,512,512,0,0, y cambios separados al mover cada componente.
3. **Cerrar el Monitor Serial y Serial Plotter**: Unity necesita acceso exclusivo a COM.
4. En el prefab Arduino, escribir el puerto (por ejemplo COM3) y dejar Baud Rate=9600. Vacío habilita búsqueda automática, que abre los puertos disponibles; indicar COM es más preciso.
5. Guardar el prefab y abrir MainMenu o Level_01. Iniciar Play. El objeto Arduino persiste al cambiar de escena.
6. Primero comprobar W/S, A/D, Space y Esc con Arduino desconectado.
7. Conectar Arduino, esperar la conexión y mover cada potenciómetro por separado. La conexión inicial no cambia la puntería.
8. Ajustar Min/Max mirando Raw, luego inversión y sensibilidad. Si hay ruido, subir los umbrales antes de aumentar el suavizado.
9. Pulsar D2 para abrir el menú; girar A0 para resaltar una opción y pulsar D3 para seleccionarla. D2 vuelve al juego. Probar también flechas/Enter/Esc.
10. Si se cambian COM o Baud Rate durante Play, desactivar y activar **solo ArduinoSerial** para reiniciar el worker; también se aplican al volver a entrar en Play.
11. Desconectar y reconectar USB durante la partida. El teclado debe seguir disponible y los botones mantenidos no deben activarse al reconectar.

Configuración manual pendiente: identificar el modelo real de placa, cablear, cargar el sketch, indicar COM y calibrar los potenciómetros reales. No se cargó firmware ni se comprobó el circuito físico automáticamente.

## Pruebas y alcance

Validación realizada: **18 pruebas aprobadas, 0 fallidas** en Unity 6000.6.0f1. Incluye la confirmación del botón real Level 1 desde MainMenu, la carga de Level_01 y la reutilización sin duplicados del menú y de los componentes Arduino.

En Unity: **Tools > Arduino > Run integration checks** (Ctrl+Shift+F8), o Test Runner, categoría CannonArduino. Guardar antes cualquier escena que se esté editando. Las pruebas cargan temporalmente Level_01, entran en Play y salen al terminar. El resultado se guarda en Logs/Arduino-test-results.xml.

Las pruebas verifican parser, rangos, ruido/picos, giros lentos, inversión, sensibilidad, arbitraje, límites y suavizado. La prueba en la escena real usa un teclado virtual del Input System y paquetes en la cola del transporte, y comprueba movimiento por eje, botones, recarga, mantener presionado, menús, confirmación con Arduino/Enter, ausencia de disparos accidentales, desconexión y nueva sesión.

La inyección de paquetes comprueba el recorrido desde la cola hasta el juego; **no sustituye una prueba de USB/puerto físico ni mide el ruido del circuito real**. El sketch se compiló y enlazó con el compilador AVR instalado y el core oficial Uno (ATmega328P): 3440 bytes de programa y 223 bytes de RAM. El firmware HEX de comprobación queda en Temp/ArduinoUnoBuild; para cargarlo normalmente se recomienda Arduino IDE y el sketch fuente.

## Archivos de esta integración

Modificados: Arduino/Arduino.ino, ArduinoSerial.cs, ArduinoController.cs, CannonController.cs, MenuManager.cs, Assets/Prefabs/Arduino.prefab, Assets/Scenes/MainMenu.unity, Assets/Scenes/Level_01.unity y ProjectSettings/EditorBuildSettings.asset.

Añadidos: Assets/Tests/Editor/CannonArduinoTests.cs (y sus metadatos Unity), esta guía y el resumen verificable de pruebas en Logs. Los cambios anteriores del proyecto y sus assets se conservan.
