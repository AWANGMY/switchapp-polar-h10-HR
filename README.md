# Polar H10 Heart Rate WinForms (C# .NET Framework 4.7.2)

This repository contains a Visual Studio 2022 WinForms desktop app for Polar H10 heart rate monitoring.

## Features

- C# WinForms desktop app
- Target framework: .NET Framework 4.7.2
- Connect / Start / Stop / Disconnect workflow
- Real-time heart rate chart (`bpm`)
- CSV export (`time,bpm`)
- Polar H10 BLE mode + simulation mode
- Dynamic Y-axis scaling for heart rate display

## Screenshot

![App Screenshot](docs/app-screenshot.png)

## Open and run

1. Open `PolarH10EcgWinForms.sln` in Visual Studio 2022.
2. Ensure the `Desktop development with .NET` workload is installed.
3. Restore NuGet packages.
4. Build and run.

## App usage

1. Keep `Simulation mode (no device)` checked to test without hardware, or uncheck to use Polar H10.
2. Keep device filter as `Polar H10` (or set your strap name).
3. Click `Connect`.
4. Click `Start` to begin heart rate streaming.
5. Click `Stop` and then `Export CSV` to save captured heart rate.
6. Click `Disconnect` when done.

## BLE implementation notes

- Real device mode uses standard BLE Heart Rate Service (`0x180D`) and Heart Rate Measurement characteristic (`0x2A37`).
- Heart rate values are parsed from characteristic notifications and plotted directly in `bpm`.
- Sampling is device-notification-driven (commonly around 1 Hz), not a PMD ECG stream.

## Simulation mode

- Simulation emits one heart-rate sample per second.
- Values follow a bounded random walk to mimic realistic resting HR variation.

## Troubleshooting connection

- Make sure no other app (Polar Beat/Flow, watch app, phone settings page) is actively holding the H10 BLE connection.
- Keep the strap worn and awake when pressing `Connect`.
- If connection fails, disconnect/reconnect and retry.
