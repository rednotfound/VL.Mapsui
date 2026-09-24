@echo off
rem Double-click me: a window lists the help patches, pick one and vvvv opens it.
rem Everything still goes through tools\Open-HelpPatch.ps1 - this is only the doorbell.
start "" pwsh -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0tools\Open-HelpPatch-GUI.ps1"
