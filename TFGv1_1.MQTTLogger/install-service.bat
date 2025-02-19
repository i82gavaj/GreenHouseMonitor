@echo off
echo Instalando MQTT Logger Service...

cd "%~dp0bin\Debug"

REM Detener el servicio si ya existe
net stop MQTTLoggerService 2>nul

REM Desinstalar versión anterior si existe
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\installutil.exe" /u "TFGv1_1.MQTTLogger.exe" 2>nul

REM Instalar nueva versión
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\installutil.exe" "TFGv1_1.MQTTLogger.exe"

REM Iniciar el servicio
net start MQTTLoggerService

echo.
echo Instalación completada.
pause