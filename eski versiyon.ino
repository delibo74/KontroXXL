#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <EEPROM.h>

LiquidCrystal_I2C lcd(0x27, 16, 2);

// PIN DEF
#define ENCODER_CLK 2
#define ENCODER_DT  4
#define ENCODER_SW  6
#define GERI_PIN    7

// MODES
#define MOD_ANAEKRAN   0
#define MOD_MENU       2
#define MOD_POOLLAR    3
#define MOD_APPS       5
#define MOD_KISAYOLLAR 9
#define MOD_NAS_POWER  11
#define MOD_BAGLANIYOR 10

int mod = MOD_BAGLANIYOR;
bool pcAcik = false;
int pag = 0;

// DATA
int winCpu=0, winRam=0, winGpu=0, winGpuT=0, winGpuF=0;
float winNet=0, winFreq=0;
int nasCpu=0, nasTemp=0, nasAlerts=0;
float nasLoad=0, nasRx=0, nasTx=0;
bool nasConn=false;

// Arrays (Statuses in RAM, Names in EEPROM to save 700 bytes of SRAM)
byte appDurum[30];   int appSayisi=0;  int appSec=0;
int poolKullanim[5]; int poolSayisi=0; int poolSec=0;
int scSayisi=0;   int scSec=0;

// REUSABLE BUFFER FOR EEPROM READS
char eeBuf[20];

int curVol=0, lastVol=-1;
unsigned long volBarTime=0;

int anaMenuSec = 0;
int nasPowerSec = 0;
int sonCLK = HIGH;
unsigned long sonButon=0, sonGeri=0, lastUpdate=0, lastDataReceived=0;

// Scroll state for app names
int scrollPos = 0;
int lastScrollIdx = -1;
unsigned long lastScrollTime = 0;

static char lbuf[17];
static char tmp[8]; // dtostrf buffer

// LCD: print text padded to 16 chars
void lcdRow(int row, const char* text) {
  lcd.setCursor(0, row);
  int len = 0;
  while (text && *text && len < 16) {
    char c = *text++;
    if (c >= 32 && c <= 126) { lcd.write(c); len++; } // Filter only printable ASCII
  }
  while (len < 16) { lcd.write(' '); len++; }
}

// Flash string version to save RAM
void lcdRow(int row, const __FlashStringHelper* text) {
  lcd.setCursor(0, row);
  lcd.print(text);
  // Manual padding for F() strings - assuming they are printed at start of line
  // or they are defined with spaces in F()
}

void clearRow(int row) {
  lcd.setCursor(0, row);
  lcd.print(F("                "));
}

// Scrolling row: resets scroll when idx changes
void scrollRow(int row, const char* text, int idx) {
  int nameLen = strlen(text);
  if (idx != lastScrollIdx) {
    scrollPos = 0;
    lastScrollIdx = idx;
    lastScrollTime = millis();
  }
  if (nameLen <= 16) {
    lcdRow(row, text);
    return;
  }
  char sb[17];
  strncpy(sb, text + scrollPos, 16);
  sb[16] = '\0';
  lcdRow(row, sb);
  if (millis() - lastScrollTime > 350) {
    lastScrollTime = millis();
    if (++scrollPos > nameLen - 16) scrollPos = 0;
  }
}

// Progress bar from col 0 with percentage: [#####....] 67%
void pBar(int v, int row) {
  int d = map(constrain(v, 0, 100), 0, 100, 0, 9);
  lcd.setCursor(0, row);
  lcd.write('[');
  for(int i=0; i<9; i++) lcd.write(i < d ? '#' : '.');
  lcd.write(']');
  char pct[6];
  snprintf(pct, 6, " %3d%%", v); // " 67%" = 5 chars → total 16
  lcd.print(pct);
}

// PARSING - pure C strings, zero heap allocation
void updateVal(const char* v, const char* k, int &target) {
  char search[20];
  snprintf(search, sizeof(search), "\"%s\"", k);
  const char* p = strstr(v, search);
  if (p) {
    const char* c = strchr(p + strlen(search), ':');
    if (c) {
      c++;
      while (*c && !isDigit(*c) && *c != '-') c++;
      if (*c) { target = (int)atol(c); lastDataReceived = millis(); }
    }
  }
}

void updateFVal(const char* v, const char* k, float &target) {
  char search[20];
  snprintf(search, sizeof(search), "\"%s\"", k);
  const char* p = strstr(v, search);
  if (p) {
    const char* c = strchr(p + strlen(search), ':');
    if (c) {
      c++;
      while (*c && !isDigit(*c) && *c != '-' && *c != '.') c++;
      if (*c) { target = atof(c); lastDataReceived = millis(); }
    }
  }
}

void getStrVal(const char* v, char* out, int maxLen) {
  const char* p = strstr(v, "\"n\":");
  if (p) {
    p += 4;
    while (*p == ' ' || *p == '"') p++;
    int i = 0;
    while (*p && *p != '"' && i < maxLen - 1) out[i++] = *p++;
    out[i] = '\0';
  }
}

// EEPROM HELPERS
// Apps: 0-510, Shortcuts: 520-600, Pools: 610-670
void saveEE(int base, int idx, int entryLen, const char* str) {
  int addr = base + (idx * entryLen);
  for (int j = 0; j < entryLen - 1; j++) {
    char c = (j < strlen(str)) ? str[j] : '\0';
    EEPROM.update(addr + j, c);
  }
  EEPROM.update(addr + entryLen - 1, '\0');
}

void loadEE(int base, int idx, int entryLen) {
  int addr = base + (idx * entryLen);
  for (int j = 0; j < entryLen - 1; j++) eeBuf[j] = EEPROM.read(addr + j);
  eeBuf[entryLen - 1] = '\0';
}

void serialIn() {
  if (Serial.available()) {
    static char buf[200]; // Increased to 200 for safe parsing of 30th app data
    int len = Serial.readBytesUntil('\n', buf, 199);
    if (len <= 0) return;
    buf[len] = '\0';
    if (len > 0 && buf[len-1] == '\r') buf[--len] = '\0';
    if (len < 10) return;

    if (strstr(buf, "\"type\":\"info\"") || strstr(buf, "\"gu\":")) {
      Serial.println(F("ACK:INFO"));
      if (!pcAcik) { pcAcik = true; lcd.backlight(); mod = MOD_ANAEKRAN; lcd.clear(); }
      int cTmp = nasConn ? 1 : 0; updateVal(buf, "c", cTmp); nasConn = (cTmp == 1);
      updateVal(buf, "wc", winCpu); updateVal(buf, "wr", winRam);
      updateFVal(buf, "wn", winNet); updateFVal(buf, "cf", winFreq);
      updateVal(buf, "gu", winGpu); updateVal(buf, "gt", winGpuT); updateVal(buf, "gf", winGpuF);
      updateVal(buf, "nc", nasCpu); updateFVal(buf, "nr", nasRx); updateFVal(buf, "ntx", nasTx);
      updateFVal(buf, "nl", nasLoad); updateVal(buf, "nt", nasTemp); updateVal(buf, "na", nasAlerts);
      
      int oldVol = curVol;
      updateVal(buf, "vol", curVol);
      if (curVol != oldVol && oldVol != -1) volBarTime = millis();
      if (lastVol == -1) lastVol = curVol; // Init
    }
    else if (strstr(buf, "\"type\":\"shutdown\"")) {
      lcd.noBacklight();
      pcAcik = false;
      mod = MOD_BAGLANIYOR;
    }
    else if (strstr(buf, "\"sc_cnt\"")) {
      updateVal(buf, "val", scSayisi);
      if (scSayisi > 6) scSayisi = 6;
    }
    else if (strstr(buf, "\"sc\"")) {
      int idx = -1; updateVal(buf, "i", idx);
      if (idx >= 0 && idx < 6) {
        char sName[13]; memset(sName, 0, 13);
        getStrVal(buf, sName, 13);
        saveEE(520, idx, 13, sName);
      }
    }
    else if (strstr(buf, "\"app_cnt\"")) {
      updateVal(buf, "val", appSayisi);
      if (appSayisi > 30) appSayisi = 30;
      // Clear status only, names will stay in EEPROM
      for(int i=0; i<30; i++) appDurum[i] = 2; // 2 = Syncing status
    }
    else if (strstr(buf, "\"app\"")) {
      int idx = -1; updateVal(buf, "i", idx);
      if (idx >= 0 && idx < 30) {
        char aName[17]; memset(aName, 0, 17);
        getStrVal(buf, aName, 17);
        saveEE(0, idx, 17, aName);
        int st = 0; updateVal(buf, "s", st); appDurum[idx] = (byte)st;
        if (idx == appSec) lastScrollIdx = -1;
      }
    }
    else if (strstr(buf, "\"pool_cnt\"")) {
      updateVal(buf, "val", poolSayisi);
      if (poolSayisi > 5) poolSayisi = 5;
    }
    else if (strstr(buf, "\"pool\"")) {
      int idx = -1; updateVal(buf, "i", idx);
      if (idx >= 0 && idx < 5) {
        char pName[11]; memset(pName, 0, 11);
        getStrVal(buf, pName, 11);
        saveEE(610, idx, 11, pName);
        updateVal(buf, "u", poolKullanim[idx]);
      }
    }
  }
}

void uiUpdate() {
  if (pcAcik && millis() - lastDataReceived > 30000) { pcAcik = false; mod = MOD_BAGLANIYOR; lcd.clear(); }

  switch(mod) {
    case MOD_BAGLANIYOR:
      lcdRow(0, F("KONTROXXL v1.0  "));
      lcdRow(1, F("ESTABLISHING... "));
      break;

    case MOD_ANAEKRAN:
      if (millis() - volBarTime < 2000) {
        lcdRow(0, F(" VOLUME CONTROL "));
        pBar(curVol, 1);
      } else if (pag == 0) {
        dtostrf(winFreq, 4, 2, tmp);
        snprintf(lbuf, 17, "CPU:%d%% %sG", winCpu, tmp);
        lcdRow(0, lbuf);
        snprintf(lbuf, 17, "RAM:%d%%", winRam);
        lcdRow(1, lbuf);
      } else if (pag == 1) {
        snprintf(lbuf, 17, "GPU:%d%% %dC", winGpu, winGpuT);
        lcdRow(0, lbuf);
        snprintf(lbuf, 17, "Fan:%d%% %dMbps", winGpuF, (int)min(winNet, 999.0f));
        lcdRow(1, lbuf);
      } else if (pag == 2) {
        if (!nasConn && nasCpu == 0) {
          lcdRow(0, F("  NAS: OFFLINE  "));
          lcdRow(1, F(" No Connection  "));
        } else {
          snprintf(lbuf, 17, "NAS:%d%% %dC", nasCpu, nasTemp);
          lcdRow(0, lbuf);
          char rxS[4], txS[4];
          snprintf(rxS, 4, "%3d", (int)min(nasRx, 999.0f));
          snprintf(txS, 4, "%3d", (int)min(nasTx, 999.0f));
          lcd.setCursor(0, 1);
          lcd.write(byte(1)); 
          lcd.print(rxS); lcd.print(F("Mb "));
          lcd.write(byte(2));
          lcd.print(txS); lcd.print(F("Mb   "));
        }
      } else {
        lcdRow(0, F("> NAS DASHBOARD "));
        if (nasAlerts == 0) lcdRow(1, F("No active alerts"));
        else { snprintf(lbuf, 17, "%d SYSTEM ALERTS!", nasAlerts); lcdRow(1, lbuf); }
      }
      break;

    case MOD_MENU:
      lcdRow(0, F("> SYSTEM MENU   "));
      if(anaMenuSec==0)      lcdRow(1, F("1. NAS APPS     "));
      else if(anaMenuSec==1) lcdRow(1, F("2. NAS POOLS    "));
      else if(anaMenuSec==2) lcdRow(1, F("3. SHORTCUTS    "));
      else                   lcdRow(1, F("4. NAS POWER    "));
      break;

    case MOD_NAS_POWER:
      lcdRow(0, F("> NAS POWER     "));
      if(nasPowerSec==0)      lcdRow(1, F("1. NAS REBOOT   "));
      else if(nasPowerSec==1) lcdRow(1, F("2. NAS SHUTDOWN "));
      else                    lcdRow(1, F("3. CANCEL       "));
      break;

    case MOD_APPS:
      if(appSayisi==0) { clearRow(0); lcdRow(1, F("Syncing...      ")); }
      else {
        loadEE(0, appSec, 17);
        scrollRow(0, eeBuf, appSec);
        if (appDurum[appSec] == 2) lcdRow(1, F(">> UPDATING <<  "));
        else lcdRow(1, appDurum[appSec]==1 ? F(">> RUNNING <<   ") : F(">> STOPPED <<   "));
      }
      break;

    case MOD_KISAYOLLAR:
      if(scSayisi==0) { clearRow(0); lcdRow(1, F("Syncing...      ")); }
      else { 
        loadEE(520, scSec, 13);
        lcdRow(0, F("ACTIONS:        ")); lcdRow(1, eeBuf); 
      }
      break;

    case MOD_POOLLAR:
      if(poolSayisi==0) { clearRow(0); lcdRow(1, F("Syncing...      ")); }
      else { 
        loadEE(610, poolSec, 11);
        lcdRow(0, eeBuf); pBar(poolKullanim[poolSec], 1); 
      }
      break;
  }
}

void setup() {
  Serial.begin(115200);
  Serial.setTimeout(200);
  lcd.init(); lcd.backlight();

  // Custom chars stored locally to avoid consuming global SRAM
  // Slot 1 = ↓ (download/RX), Slot 2 = ↑ (upload/TX)
  // NOT slot 0 — byte(0)=='\0' breaks C-string functions
  byte downArr[8] = {B00000,B00100,B00100,B00100,B11111,B01110,B00100,B00000};
  byte upArr[8]   = {B00000,B00100,B01110,B11111,B00100,B00100,B00100,B00000};
  lcd.createChar(1, downArr);
  lcd.createChar(2, upArr);

  pinMode(ENCODER_CLK, INPUT_PULLUP); pinMode(ENCODER_DT, INPUT_PULLUP);
  pinMode(ENCODER_SW, INPUT_PULLUP); pinMode(GERI_PIN, INPUT_PULLUP);
  sonCLK = digitalRead(ENCODER_CLK); lcd.clear();
  Serial.println(F("CMD:READY"));
}

void loop() {
  serialIn();
  unsigned long t = millis();
  int clk = digitalRead(ENCODER_CLK);
  if (clk != sonCLK && clk == LOW) {
    int dt = digitalRead(ENCODER_DT); int d = (dt == HIGH ? 1 : -1);
    if (mod == MOD_ANAEKRAN)   { if (d == 1) Serial.println(F("CMD:VOL_UP")); else Serial.println(F("CMD:VOL_DN")); }
    else if (mod == MOD_MENU)          anaMenuSec = (anaMenuSec + d + 4) % 4;
    else if (mod == MOD_NAS_POWER)     nasPowerSec = (nasPowerSec + d + 3) % 3;
    else if (mod == MOD_APPS       && appSayisi > 0) { appSec = (appSec + d + appSayisi) % appSayisi; lastScrollIdx = -1; }
    else if (mod == MOD_KISAYOLLAR && scSayisi  > 0) scSec  = (scSec  + d + scSayisi)  % scSayisi;
    else if (mod == MOD_POOLLAR    && poolSayisi > 0) poolSec = (poolSec + d + poolSayisi) % poolSayisi;
  }
  sonCLK = clk;

  // BUTTONS
  if (digitalRead(ENCODER_SW) == LOW && t - sonButon > 250) {
    sonButon = t;
    if (mod == MOD_ANAEKRAN) { mod = MOD_MENU; anaMenuSec = 0; lcd.clear(); Serial.println(F("CMD:UPDATE")); }
    else if (mod == MOD_MENU) {
      if (anaMenuSec == 0)      { mod = MOD_APPS;       appSec = 0; lastScrollIdx = -1; Serial.println(F("CMD:APPS")); }
      else if (anaMenuSec == 1) { mod = MOD_POOLLAR;    poolSec = 0; Serial.println(F("CMD:POOLS")); }
      else if (anaMenuSec == 2) { mod = MOD_KISAYOLLAR; scSec = 0; Serial.println(F("CMD:SHORTCUTS")); }
      else if (anaMenuSec == 3) { mod = MOD_NAS_POWER;  nasPowerSec = 0; }
      lcd.clear();
    }
    else if (mod == MOD_NAS_POWER) {
      if (nasPowerSec == 0)      { Serial.println(F("CMD:NAS_REBOOT"));  mod = MOD_ANAEKRAN; }
      else if (nasPowerSec == 1) { Serial.println(F("CMD:NAS_SHUTDOWN")); mod = MOD_ANAEKRAN; }
      else { mod = MOD_MENU; }
      lcd.clear();
    }
    else if (mod == MOD_KISAYOLLAR && scSayisi > 0) {
      Serial.print(F("CMD:RUN_IDX:")); Serial.println(scSec);
      mod = MOD_ANAEKRAN; lcd.clear();
    }
    else if (mod == MOD_APPS && appSayisi > 0) {
      Serial.print(F("CMD:APP_IDX:")); Serial.print(appSec);
      Serial.println(appDurum[appSec] == 1 ? ":STOP" : ":START");
    }
  }

  if (digitalRead(GERI_PIN) == LOW && t - sonGeri > 300) {
    sonGeri = t;
    if (mod == MOD_ANAEKRAN) { 
      pag = (pag + 1) % 4; lcd.clear();
    } else {
      mod = MOD_ANAEKRAN; lcd.clear();
    }
  }

  // PAGE CHANGE (Double click or any other way? Let's use internal auto-loop or just 
  // let user stay on screen. Or: Geri button long press = Page change)
  // For now, let's keep it simple: Geri on sub-menus goes back to Home.
  
  if (t - lastUpdate > 500) { lastUpdate = t; uiUpdate(); }

  static unsigned long lastHb = 0;
  if (t - lastHb > 10000) { lastHb = t; Serial.println(F("CMD:ALIVE")); }

  delay(1); // encoder parazit toleransı
}
