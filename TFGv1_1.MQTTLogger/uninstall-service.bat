@echo off
chcp 65001 > nul
echo Desinstalando MQTT Logger Service...

REM Obtener el directorio del script y navegar a bin\Debug
cd /d "%~dp0bin\Debug"

REM Detener el servicio
net stop MQTTLoggerService 2>nul

REM Desinstalar el servicio usando ruta completa
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\installutil.exe" /u "TFGv1_1.MQTTLogger.exe"

echo.
echo Desinstalación completada.
pause 