@echo off
rem Installs Harbor for the current user (no admin rights): %LOCALAPPDATA%\Programs\Harbor,
rem a Start-menu shortcut, an Apps & features entry, and starts it.
start "" "%~dp0Harbor.exe" --install
