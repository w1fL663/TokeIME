# TokeIME

Official TokeIME build with an English user interface.

TokeIME is a Windows tray utility that blocks selected keyboard shortcuts while a configured program is in the foreground. It can block input-method switching shortcuts or any custom shortcut you define.

## Features

- Automatic effect when a configured program is in the foreground
- Add currently running programs from an icon list
- Global search across running programs, Start Menu and installed applications
- Desktop search for shortcuts and EXEs, including desktop folders
- Custom blocked shortcuts with direct key capture, including Tab
- No DLL injection and no network access

## Run

```text
TokeIME.exe
```

Manual launch opens Settings. Use `--startup` for a silent tray-only launch.

## Configuration

Configuration directory:

```text
%APPDATA%\TokeIME\config.json
```

The Windows startup entry is stored as `TokeIME`.

## Build

From this folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## Notes

- Added programs and custom shortcuts are stored locally.
- It runs best in Win11.
