// ENT-Game: dos potenciómetros y dos pulsadores, protocolo C1.
// Cambiar los pines aquí; deben coincidir con el cableado, no con números en Unity.
const uint8_t PIN_VERTICAL = A0;
const uint8_t PIN_HORIZONTAL = A1;
const uint8_t PIN_MENU = 2;
const uint8_t PIN_FIRE = 3;
const unsigned long BAUD_RATE = 9600;
const unsigned long SEND_INTERVAL_MS = 25; // 40 Hz; cabe incluso a 9600 baudios.
const unsigned long DEBOUNCE_MS = 25;

struct Button {
  uint8_t pin;
  bool candidate;
  bool pressed;
  unsigned long changedAt;
};

Button menuButton = {PIN_MENU, false, false, 0};
Button fireButton = {PIN_FIRE, false, false, 0};
unsigned long lastSent = 0;

void updateButton(Button &button, unsigned long now) {
  bool reading = digitalRead(button.pin) == LOW;
  if (reading != button.candidate) {
    button.candidate = reading;
    button.changedAt = now;
  }
  if (now - button.changedAt >= DEBOUNCE_MS)
    button.pressed = button.candidate;
}

void setup() {
  pinMode(PIN_MENU, INPUT_PULLUP);
  pinMode(PIN_FIRE, INPUT_PULLUP);
  // Un botón mantenido al encender no se interpreta como una nueva pulsación.
  menuButton.candidate = menuButton.pressed = digitalRead(PIN_MENU) == LOW;
  fireButton.candidate = fireButton.pressed = digitalRead(PIN_FIRE) == LOW;
  Serial.begin(BAUD_RATE);
}

void loop() {
  unsigned long now = millis();
  updateButton(menuButton, now);
  updateButton(fireButton, now);
  if (now - lastSent < SEND_INTERVAL_MS) return;
  lastSent = now;
  // CSV ASCII con prefijo de versión y fin de línea: C1,vertical,horizontal,menu,fire
  Serial.print("C1,");
  Serial.print(analogRead(PIN_VERTICAL));
  Serial.print(',');
  Serial.print(analogRead(PIN_HORIZONTAL));
  Serial.print(',');
  Serial.print(menuButton.pressed ? 1 : 0);
  Serial.print(',');
  Serial.println(fireButton.pressed ? 1 : 0);
}
