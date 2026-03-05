# Polar H10 ECG WinForms (C# .NET Framework 4.7.2)

This repository contains a Visual Studio 2022 WinForms app that matches the assignment requirements:

- C# WinForms desktop app
- Target framework: .NET Framework 4.7.2
- Output type: Windows Application
- GUI with Connect / Start / Stop / Disconnect
- Real-time ECG chart display
- ECG collection and CSV export
- Selectable sampling frequency (130 / 200 / 260 Hz)
- Data source abstraction with Polar H10 BLE path and simulation mode

## Screenshot

![App Screenshot](docs/app-screenshot.png)

## Open and run

1. Open `PolarH10EcgWinForms.sln` in Visual Studio 2022.
2. Ensure the `Desktop development with .NET` workload is installed.
3. Restore NuGet packages.
4. Build and run.

## App usage

1. Select sample frequency from `Sample Hz`.
2. Keep `Simulation mode (no device)` checked to demo without hardware.
3. Click `Connect`.
4. Click `Start` to stream and plot real-time ECG waveform.
5. Click `Stop` and then `Export CSV` to save collected data.
6. Click `Disconnect` when done.

## Use with Polar H10

1. Uncheck `Simulation mode (no device)`.
2. Keep device filter as `Polar H10` (or update to your strap name).
3. Ensure Bluetooth is enabled and the strap is worn (electrodes wet).
4. Click `Connect`, then `Start`.

## Notes

- BLE ECG uses Polar PMD service UUIDs and control/data characteristics in `Services/PmdProtocol.cs`.
- If your Windows environment blocks BLE discovery or GATT access, run Visual Studio as administrator and confirm Bluetooth permissions.
- If a selected frequency is unsupported by device firmware, start may fail and you can retry with another frequency.
