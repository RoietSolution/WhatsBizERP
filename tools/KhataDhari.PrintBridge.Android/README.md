# KhataDhari Print Bridge (MT580P POC)

Standalone Android proof of concept for testing native Bluetooth Classic RFCOMM/SPP text printing. It does not integrate with WhatsBiz POS and contains no invoice or GST logic.

## Build prerequisites

- Android Studio with Android SDK Platform 36 and Android SDK Build Tools 36.0.0 installed.
- JDK 17.
- Android Gradle Plugin 9.4.0 and the checked-in Gradle Wrapper use Gradle 9.6.0. No global Gradle installation is required.

Build a debug APK from this folder:

```text
.\gradlew.bat :app:assembleDebug
```

Expected APK:

```text
app/build/outputs/apk/debug/app-debug.apk
```

Install manually over USB after enabling USB debugging:

```text
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

## Physical test procedure

1. Pair the MT580P from Android Settings and ensure the printer is powered and nearby.
2. Install `app-debug.apk`.
3. Open **KhataDhari Print Bridge** and grant the requested **Nearby devices** permission.
4. Tap **Refresh Paired Devices**.
5. Select **MT580P** from the paired-device selector.
6. Tap **Connect** and confirm the status becomes **Connected**.
7. Tap **Print Test Receipt** and inspect the paper.
8. Tap **Disconnect**.

Report the physical result as one of:

- **A. Connected + printed correctly**
- **B. Connected + garbage output**
- **C. Connected + no paper output**
- **D. Could not connect**
- **E. Permission/device error**

The RFCOMM connection uses the standard SPP UUID `00001101-0000-1000-8000-00805F9B34FB`. The observed BLE services are deliberately not used by this first POC. Successful socket writes or a successful build do not prove printer compatibility; physical paper output is required.
