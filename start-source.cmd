@echo off
cd /d "%~dp0"
echo Open http://localhost:5080 after the server starts.
dotnet run --launch-profile HealthCare
pause
