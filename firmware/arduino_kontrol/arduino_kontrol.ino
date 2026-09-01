#include <Wire.h>
#include <LiquidCrystal_I2C.h>

LiquidCrystal_I2C lcd(0x27, 16, 2);

// PIN DEF
#define ENCODER_CLK 2
#define ENCODER_DT  4
#define ENCODER_SW  6
#define GERI_PIN    7

volatile int lastCLK = HIGH;
unsigned long lastBtn = 0, lastGeri = 0;
byte fullBlock[8] = {B11111,B11111,B11111,B11111,B11111,B11111,B11111,B11111};

// F22: Fixed-size char buffer replaces Arduino String — eliminates heap fragmentation
#define BUF_SIZE 32
char inputBuffer[BUF_SIZE];
uint8_t bufIdx = 0;

void setup() {
  Serial.begin(115200);
  Wire.begin();
  Wire.setClock(400000);
  lcd.init();
  
  // Custom Chars
  byte downArr[8] = {B00000,B00100,B00100,B00100,B11111,B01110,B00100,B00000};
  byte upArr[8]   = {B00000,B00100,B01110,B11111,B00100,B00100,B00100,B00000};
  lcd.createChar(0, fullBlock);
  lcd.createChar(1, downArr);
  lcd.createChar(2, upArr);

  pinMode(ENCODER_CLK, INPUT_PULLUP);
  pinMode(ENCODER_DT, INPUT_PULLUP);
  pinMode(ENCODER_SW, INPUT_PULLUP);
  pinMode(GERI_PIN, INPUT_PULLUP);

  attachInterrupt(digitalPinToInterrupt(ENCODER_CLK), encoderInt, CHANGE);
  
  fadeInEffect();
  Serial.println(F("CMD:READY"));
}

void fadeInEffect() {
  lcd.noBacklight();
  lcd.clear();
  for(int i=0; i<16; i++) {
    lcd.setCursor(i, 0); lcd.write((uint8_t)0);
    lcd.setCursor(i, 1); lcd.write((uint8_t)0);
  }
  lcd.backlight();
  delay(100);
  for(int i=0; i<16; i++) {
    lcd.setCursor(i, 0); lcd.print(" ");
    lcd.setCursor(i, 1); lcd.print(" ");
    delay(30);
  }
  lcd.setCursor(0,0);
  lcd.print(F(" KONTROXXL V7.8 "));
  lcd.setCursor(0,1);
  lcd.print(F(" THIN CLIENT... "));
}

void fadeOutEffect() {
  for(int i=0; i<16; i++) {
    lcd.setCursor(i, 0); lcd.write((uint8_t)0);
    lcd.setCursor(i, 1); lcd.write((uint8_t)0);
    delay(30);
  }
  delay(200);
  lcd.noBacklight();
  lcd.clear();
}

void encoderInt() {
  static int encoderStep = 0;
  int clk = digitalRead(ENCODER_CLK);
  if (clk != lastCLK) {
    if (digitalRead(ENCODER_DT) != clk) encoderStep++;
    else encoderStep--;
    if (abs(encoderStep) >= 2) {
      if (encoderStep > 0) Serial.println(F("EV:UP"));
      else Serial.println(F("EV:DN"));
      encoderStep = 0;
    }
  }
  lastCLK = clk;
}

unsigned long lastSerialTime = 0;
bool lcdPowerState = true;

void loop() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    lastSerialTime = millis();
    
    // Auto-Wake
    if (!lcdPowerState) { 
      lcdPowerState = true; 
      lcd.backlight(); 
    }
    
    if (c == '\n') {
      inputBuffer[bufIdx] = '\0';
      handleCommand(inputBuffer);
      bufIdx = 0;
    } else if (c != '\r') {
      // F22: no heap allocation — just write to stack array
      if (bufIdx < BUF_SIZE - 1) inputBuffer[bufIdx++] = c;
    }
  }

  unsigned long t = millis();
  
  // Auto-Shutdown Watchdog
  if (lcdPowerState && (t - lastSerialTime > 20000)) {
    fadeOutEffect();
    lcdPowerState = false;
  }

  // Handshake
  static bool linked = false;
  if (!linked && t > 2000) { Serial.println(F("CMD:READY")); linked = true; }

  // Simple Debounce for Buttons
  if (digitalRead(ENCODER_SW) == LOW && (t - lastBtn > 350)) {
    Serial.println(F("EV:CLICK"));
    lastBtn = t;
  }
  if (digitalRead(GERI_PIN) == LOW && (t - lastGeri > 350)) {
    Serial.println(F("EV:BACK"));
    lastGeri = t;
  }
}

// F22: handleCommand now accepts char* — no String heap allocation
void handleCommand(const char* line) {
  if (line == NULL || line[0] == '\0') return;

  if (strcmp(line, "OFF") == 0) { fadeOutEffect(); }
  else if (strcmp(line, "ON") == 0) { lcd.backlight(); fadeInEffect(); }
  else if (strncmp(line, "L0=", 3) == 0) { 
    lcd.setCursor(0,0); 
    lcdPad(line + 3);
  }
  else if (strncmp(line, "L1=", 3) == 0) { 
    lcd.setCursor(0,1);
    const char* content = line + 3;
    for(int i = 0; i < 16; i++){
      if(content[i] != '\0'){
        if((uint8_t)content[i] == 0x01) lcd.write((uint8_t)1);
        else if((uint8_t)content[i] == 0x02) lcd.write((uint8_t)2);
        else lcd.print(content[i]);
      } else {
        lcd.print(' ');
      }
    }
  }
  else if (strcmp(line, "CLR") == 0) { lcd.clear(); }
  else if (strncmp(line, "B1=", 3) == 0) { 
    drawBar(atoi(line + 3)); 
  }
}

// F23: Write directly to LCD — no String allocation needed
void lcdPad(const char* s) {
  int i = 0;
  for (; i < 16 && s[i] != '\0'; i++) lcd.print(s[i]);
  for (; i < 16; i++) lcd.print(' ');
}

void drawBar(int v) {
  lcd.setCursor(0, 1);
  int d = map(constrain(v, 0, 100), 0, 100, 0, 9);
  lcd.write('[');
  for(int i=0; i<9; i++) lcd.write(i < d ? '#' : '.');
  lcd.write(']');
  lcd.print(" ");
  if(v < 100) lcd.print(" ");
  if(v < 10) lcd.print(" ");
  lcd.print(v);
  lcd.print("%");
}
