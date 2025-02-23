@echo off
echo Desinstalando MQTT Logger Service...

REM Asegurarse de que estamos en el directorio correcto
cd /d "%~dp0bin\Debug"

REM Verificar si existe el archivo
if not exist "TFGv1_1.MQTTLogger.exe" (
    echo Error: No se encuentra TFGv1_1.MQTTLogger.exe
    pause
    exit /b 1
)

REM Detener el servicio
echo Deteniendo el servicio...
net stop MQTTLoggerService 2>nul

REM Esperar un momento
timeout /t 2 /nobreak

REM Desinstalar el servicio
echo Desinstalando el servicio...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe" /u "TFGv1_1.MQTTLogger.exe"

REM Eliminar el servicio del sistema
sc delete MQTTLoggerService 2>nul

echo.
echo Desinstalación completada.
pause 