# Polar H10 ECG WinForms (C# .NET Framework 4.7.2)

This repository contains a Visual Studio 2022 WinForms app that matches the assignment requirements:

- C# WinForms desktop app
- Target framework: .NET Framework 4.7.2
- Output type: Windows Application
- GUI with Connect / Start / Stop / Disconnect
- Real-time ECG chart display
- ECG collection and CSV export
- Data source abstraction with Polar H10 BLE path and simulation mode

## Open and run

1. Open `PolarH10EcgWinForms.sln` in Visual Studio 2022.
2. Ensure the `Desktop development with .NET` workload is installed.
3. Restore NuGet packages.
4. Build and run.

## App usage

1. Keep `Use simulation` checked to demo without hardware.
2. Click `Connect`.
3. Click `Start` to stream and plot real-time ECG waveform.
4. Click `Stop` and then `Export CSV` to save collected data.
5. Click `Disconnect` when done.

## Use with Polar H10

1. Uncheck `Use simulation`.
2. Keep device filter as `Polar H10` (or update to your strap name).
3. Ensure Bluetooth is enabled and the strap is worn (electrodes wet).
4. Click `Connect`, then `Start`.

## Notes

- BLE ECG uses Polar PMD service UUIDs and control/data characteristics in `Services/PmdProtocol.cs`.
- If your Windows environment blocks BLE discovery or GATT access, run Visual Studio as administrator and confirm Bluetooth permissions.
- Build validation in this environment failed because `Microsoft.NET.Sdk.WindowsDesktop` is not installed locally.
