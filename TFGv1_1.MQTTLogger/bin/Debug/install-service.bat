@echo off
echo Instalando MQTT Logger Service...

REM Asegurarse de que estamos en el directorio correcto
cd /d "%~dp0bin\Debug"

REM Verificar si existe el archivo
if not exist "TFGv1_1.MQTTLogger.exe" (
    echo Error: No se encuentra TFGv1_1.MQTTLogger.exe
    echo Asegúrese de compilar el proyecto primero
    pause
    exit /b 1
)

REM Detener el servicio si ya existe
net stop MQTTLoggerService 2>nul
sc delete MQTTLoggerService 2>nul

REM Esperar un momento
timeout /t 2 /nobreak

REM Instalar el servicio
echo Instalando el servicio...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe" "TFGv1_1.MQTTLogger.exe"

REM Esperar un momento
timeout /t 2 /nobreak

REM Iniciar el servicio
echo Iniciando el servicio...
net start MQTTLoggerService

echo.
echo Instalación completada.
pause