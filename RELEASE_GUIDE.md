# AmaScan — How to Release an Updated APK

This guide walks you through building a new version of the app and putting it on the
APAI server so installed devices auto-update.

**Versioning is fully automatic — you never type, edit, or bump a version number.**

## How the auto-update works (the short version)

When a device opens the login screen, the app reads the file
`scannerapk/version.json` from the server. If the **version** in that file is higher
than the version installed on the device, the app offers to download and install the
new APK (named in the same file) from the server's `scannerapk` folder.

So the server side is just **two static files in the `scannerapk` folder**:
`version.json` and the APK it points to. There is no API endpoint to maintain.

Every **Release** build automatically stamps an ever-increasing version derived from
the build date/time, so:
- the in-app update always sees the new build as newer and fires, and
- Android always accepts the upgrade (the internal `versionCode` also increases).

There is nothing to bump by hand — building is enough.

---

## Step 1 — Build the new release

Build the project in **Release** — either in Visual Studio (set the configuration to
Release and Build), or from PowerShell in the project root:

```powershell
dotnet build .\AmaScan\AmaScan.csproj -c Release -f net8.0-android
```

That's all. The build automatically:
1. Stamps an ever-increasing version (e.g. `2026.0602.0834.36`) into the APK.
2. Writes a matching **`version.json`** right next to the APK.

Both land in:

```
AmaScan\bin\Release\net8.0-android\
    syncflo_AmaScan.syncflo_AmaScan-Signed.apk   <- the APK
    version.json                                  <- { "version": "...", "apkFileName": "..." }
```

The `version` and `apkFileName` in `version.json` always match the APK that was just
built — you never edit it.

> If the build fails, read the error — it's almost always a missing .NET workload
> (`dotnet workload restore` from the project root) or a code error.

---

## Step 2 — Copy BOTH files to the server's `scannerapk` folder

From `AmaScan\bin\Release\net8.0-android\`, copy these two files into the server's
**`scannerapk`** folder (the same folder previous APKs live in):

- **`syncflo_AmaScan.syncflo_AmaScan-Signed.apk`**
- **`version.json`**

That's the whole server step. The APK filename stays the same every release, so you're
just overwriting the previous two files. The app reads `version.json`, sees the new
version, and downloads the APK it names:

```
http://<server>/scannerapk/version.json                          <- the app checks this
http://<server>/scannerapk/syncflo_AmaScan.syncflo_AmaScan-Signed.apk  <- the app downloads this
```

(On-site the server is `175.25.97.2:8079`; off-site `192.168.0.106:8052`.)

---

## Step 3 — Verify

1. In a browser, open `http://<server>/scannerapk/version.json` — confirm it shows the
   new version.
2. On a device running the **old** version, open the app. You should be prompted:
   *"Version … is available…"*. Tap **Update**, watch the progress bar, and confirm the
   install completes.

If a device says **"App not installed"**, the new APK was signed with a different key
than the installed app — see the warning below.

---

## ⚠️ Important: signing key must never change

Android only installs an update if the new APK is signed with the **same key** as the
app already on the device. Just **don't change the keystore/signing settings between
releases**. If you ever switch keys, every device must uninstall and reinstall manually.
This applies to the very first version you install on devices too: sign it with the key
you'll keep using.

---

## Quick reference

1. Build **Release** (Visual Studio, or `dotnet build .\AmaScan\AmaScan.csproj -c Release -f net8.0-android`).
2. Copy **both** files from `AmaScan\bin\Release\net8.0-android\` —
   `syncflo_AmaScan.syncflo_AmaScan-Signed.apk` and `version.json` — into the server's
   `scannerapk\` folder.

Done. Versioning and `version.json` are produced by the build; nothing to edit.
