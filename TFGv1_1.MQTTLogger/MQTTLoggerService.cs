using System;
using System.ServiceProcess;
using System.Security.Authentication;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Protocol;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using TFGv1_1.Models;
using System.Linq;
using System.Data.Entity;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;

namespace TFGv1_1.MQTTLogger
{
    public partial class MQTTLoggerService : ServiceBase
    {
        private IMqttClient mqttClient;
        private string logDirectory;
        private readonly string connectionString;
        private Dictionary<string, DateTime> lastMessageTimes = new Dictionary<string, DateTime>();
        private const int INACTIVITY_THRESHOLD_SECONDS = 30; // Tiempo sin mensajes para considerar inactividad
        
        private Dictionary<int, SensorCalibrationInfo> sensorCalibrations = new Dictionary<int, SensorCalibrationInfo>();
        private Dictionary<int, AlertConfigurationInfo> alertConfigurations = new Dictionary<int, AlertConfigurationInfo>();
        private DateTime lastConfigRefresh = DateTime.MinValue;
        private const int CONFIG_REFRESH_INTERVAL_MINUTES = 5; // Intervalo para refrescar configuraciones

        // Definir los enums necesarios
        private enum AlertSeverity
        {
            Low,
            Medium,
            High,
            Critical
        }

        private enum SensorType
        {
            Temperature, // 0
            CO2,         // 1
            Brightness,  // 2
            Humidity     // 3
        }

        public MQTTLoggerService()
        {
            InitializeComponent();
            this.ServiceName = "MQTTLoggerService";
            
            // Obtener la ruta del ejecutable del servicio
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string baseDirectory = Path.GetDirectoryName(exePath);
            
            // Subir dos niveles para llegar a la raíz del proyecto TFGv1_1
            string projectRoot = Path.GetFullPath(Path.Combine(baseDirectory, @"..\..\.."));
            
            // Configurar la ruta de logs dentro del proyecto TFGv1_1
            this.logDirectory = Path.Combine(projectRoot, "TFGv1_1", "Logs");
            
            // Configurar la cadena de conexión
            this.connectionString = @"Data Source=DELPRADO\SQLEXPRESS;Initial Catalog=aspnet-TFGv1_1-202501301107286;Integrated Security=True;TrustServerCertificate=True";
        }

        // Añadimos las clases que faltan
        private class SensorCalibrationInfo
        {
            public int SensorID { get; set; }
            public int SensorType { get; set; }
            public List<double> RecentValues { get; set; } = new List<double>();
            public double ConversionFactor { get; set; } = 1.0;
            public bool IsCalibrated { get; set; } = false;
            public int ValueFormat { get; set; } = 0;
            public int SampleCount { get; set; } = 0;
        }

        private class AlertConfigurationInfo
        {
            public int AlertID { get; set; }
            public int SensorID { get; set; }
            public string ThresholdRange { get; set; }
            public bool NotifyByEmail { get; set; }
            public bool NotifyByPush { get; set; }
            public int Severity { get; set; }
            public DateTime CreatedAt { get; set; }
            public double? MinValue { get; set; }
            public double? MaxValue { get; set; }
        }

        protected override void OnStart(string[] args)
        {
            try 
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // Asegurarse de que exista el directorio App_Data
                string appDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data");
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }
                
                // Limpiar el diccionario al iniciar el servicio
                lastMessageTimes.Clear();
                
                LogMessage("service.log", "INFO", "Servicio iniciando...");
                
                Task.Run(async () => 
                {
                    try
                    {
                        await LogDatabaseStatus();
                        
                        // Refrescar configuraciones inmediatamente
                        LogMessage("service.log", "INFO", "Refrescando configuraciones iniciales...");
                        await RefreshConfigurations();
                        
                        // Ejecutar limpieza de alertas inmediatamente
                        using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                        {
                            await connection.OpenAsync();
                            LogMessage("service.log", "INFO", "Ejecutando limpieza de alertas inicial...");
                            await CleanupOrphanedAlerts(connection);
                        }
                        
                        // Continuar con la inicialización normal
                        await StartMQTTClient();
                        
                        // Iniciar timer para refrescar configuraciones
                        StartConfigRefreshTimer();
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error.log", "ERROR", $"Error durante la inicialización: {ex.Message}", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                using (var eventLog = new System.Diagnostics.EventLog("Application"))
                {
                    eventLog.Source = "MQTTLoggerService";
                    eventLog.WriteEntry($"Error al iniciar el servicio: {ex.Message}", 
                                      System.Diagnostics.EventLogEntryType.Error);
                }
                LogMessage("error.log", "ERROR", "Error al iniciar el servicio", ex);
            }
        }

        protected override void OnStop()
        {
            try
            {
                if (mqttClient != null && mqttClient.IsConnected)
                {
                    var logFiles = Directory.GetFiles(logDirectory, "*.log");
                    var endSessionMessage = new StringBuilder();
                    endSessionMessage.Insert(0, "════════════════════════════════════════\n");
                    endSessionMessage.Insert(0, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FIN DE SESIÓN\n");
                    endSessionMessage.Insert(0, "════════════════════════════════════════\n");

                    foreach (var logFile in logFiles)
                    {
                        PrependToFile(logFile, endSessionMessage.ToString());
                    }

                    mqttClient.DisconnectAsync().Wait();
                    LogMessage("service.log", "INFO", "Servicio detenido correctamente");
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al detener el servicio", ex);
            }
        }

        private void PrependToFile(string filePath, string content)
        {
            const int maxRetries = 3;
            int retryCount = 0;
            
            while (retryCount < maxRetries)
            {
                try
                {
                    string existingContent = "";
                    if (File.Exists(filePath))
                    {
                        existingContent = File.ReadAllText(filePath);
                    }
                    // El orden de la concatenación (content + existingContent) asegura que el nuevo contenido
                    // se añade al principio del archivo, seguido por el contenido existente
                    File.WriteAllText(filePath, content + existingContent);
                    return;
                }
                catch (IOException) when (retryCount < maxRetries - 1)
                {
                    retryCount++;
                    Task.Delay(100).Wait();
                }
                catch (Exception ex)
                {
                    File.AppendAllText(
                        Path.Combine(logDirectory, "error.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Error escribiendo en {filePath}: {ex.Message}\n"
                    );
                    break;
                }
            }
        }

        private void LogMessage(string logFile, string type, string message, Exception ex = null)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = new StringBuilder();
            
            // Construimos el mensaje en orden inverso
            if (ex != null)
            {
                logEntry.Insert(0, "════════════════════════════════════════\n");
                logEntry.Insert(0, $"  StackTrace: {ex.StackTrace}\n");
                logEntry.Insert(0, $"  Message: {ex.Message}\n");
                logEntry.Insert(0, "ERROR DETAILS:\n");
                logEntry.Insert(0, message + "\n");
                logEntry.Insert(0, $"[{timestamp}] [{type}]\n");
                logEntry.Insert(0, "════════════════════════════════════════\n");
            }
            else
            {
                logEntry.Insert(0, "════════════════════════════════════════\n");
                logEntry.Insert(0, message + "\n");
                logEntry.Insert(0, $"[{timestamp}] [{type}]\n");
                logEntry.Insert(0, "════════════════════════════════════════\n");
            }
            
            PrependToFile(Path.Combine(logDirectory, logFile), logEntry.ToString());
        }

        private string GetGreenHouseDirectory(string topic)
        {
            try
            {
                // El topic tiene el formato "GH_ID/sensor_id"
                string greenhouseId;
                
                if (topic.Contains("/"))
                {
                    greenhouseId = topic.Split('/')[0];
                }
                else
                {
                    // Si el topic no tiene el formato esperado, usar un directorio genérico
                    greenhouseId = "unknown";
                    LogMessage("error.log", "WARNING", $"Formato de topic inesperado: {topic}, usando directorio genérico");
                }
                
                var greenhousePath = Path.Combine(logDirectory, greenhouseId);
                
                // Crear el directorio si no existe
                if (!Directory.Exists(greenhousePath))
                {
                    Directory.CreateDirectory(greenhousePath);
                    LogMessage("service.log", "INFO", $"Creado nuevo directorio para invernadero: {greenhouseId}");
                }
                
                return greenhousePath;
            }
            catch (Exception ex)
            {
                // Si hay algún error, usar el directorio de logs por defecto
                LogMessage("error.log", "ERROR", $"Error al obtener directorio para topic {topic}: {ex.Message}", ex);
                return logDirectory;
            }
        }

        private async Task HandleMqttMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            // Usar un token de cancelación para evitar bloqueos
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                // Establecer un timeout para el procesamiento del mensaje
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                
                try
                {
                    // Procesar el mensaje con un timeout
                    await Task.Run(async () => 
                    {
                        try
                        {
                            var topic = e.ApplicationMessage.Topic;
                            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                            var timestamp = DateTime.Now;

                            // Registrar recepción de mensaje para depuración
                            LogMessage("debug.log", "DEBUG", $"Mensaje MQTT recibido - Topic: {topic}, Payload: {payload}");

                            // Obtener el directorio específico para este greenhouse
                            var greenhouseDirectory = GetGreenHouseDirectory(topic);
                            
                            // Verificar si es el primer mensaje para este topic
                            if (!lastMessageTimes.ContainsKey(topic))
                            {
                                var startSessionMessage = new StringBuilder();
                                startSessionMessage.Insert(0, "════════════════════════════════════════\n");
                                startSessionMessage.Insert(0, $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] INICIO DE RECEPCIÓN DE DATOS\n");
                                startSessionMessage.Insert(0, "════════════════════════════════════════\n");
                                
                                var fileName = topic.Replace("/", "_") + ".log";
                                var logPath = Path.Combine(greenhouseDirectory, fileName);
                                PrependToFile(logPath, startSessionMessage.ToString());
                            }

                            // Actualizar el tiempo del último mensaje
                            lastMessageTimes[topic] = timestamp;

                            // Procesar el payload para manejar correctamente los decimales
                            payload = NormalizeDecimalSeparator(payload);
                            
                            // Limitar el tamaño del log para evitar sobrecarga
                            string truncatedPayload = payload;
                            if (payload.Length > 100)
                            {
                                truncatedPayload = payload.Substring(0, 97) + "...";
                            }
                            
                            LogMessage("debug.log", "DEBUG", $"Payload normalizado: '{truncatedPayload}'");

                            // Procesar el valor del sensor
                            await ProcessSensorValue(topic, payload, timestamp, greenhouseDirectory);

                            // Iniciar el temporizador de inactividad
                            StartInactivityTimer(topic);
                        }
                        catch (Exception ex)
                        {
                            LogMessage("error.log", "ERROR", "Error al procesar mensaje MQTT", ex);
                        }
                    }, cts.Token);
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    LogMessage("error.log", "WARNING", 
                        $"Timeout al procesar mensaje MQTT para topic: {e.ApplicationMessage.Topic}. " +
                        "El procesamiento tomó demasiado tiempo.");
                }
                catch (Exception ex)
                {
                    LogMessage("error.log", "ERROR", "Error grave al procesar mensaje MQTT", ex);
                }
            }
        }

        private async Task ProcessSensorValue(string topic, string payload, DateTime timestamp, string greenhouseDirectory)
        {
            double sensorValue = 0;
            bool isValidNumber = false;

            if (payload.Contains('.') || payload.Contains(','))
            {
                // Intentar interpretarlo con diferentes formatos de cultura
                var cultures = new[] { 
                    System.Globalization.CultureInfo.InvariantCulture,  // Punto como separador decimal
                    new System.Globalization.CultureInfo("es-ES"),      // Coma como separador decimal
                    new System.Globalization.CultureInfo("en-US")       // Punto como separador decimal
                };

                foreach (var culture in cultures)
                {
                    if (double.TryParse(payload, System.Globalization.NumberStyles.Any, culture, out sensorValue))
                    {
                        isValidNumber = true;
                        LogMessage("alerts.log", "INFO", 
                            $"Valor convertido usando cultura {culture.Name}: '{payload}' -> {sensorValue}");
                        break;
                    }
                }

                // Si sigue sin ser válido, intentar una limpieza más agresiva
                if (!isValidNumber)
                {
                    // Extraer solo dígitos, punto y coma
                    var cleanPayload = new string(payload.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                    // Reemplazar comas por puntos
                    cleanPayload = cleanPayload.Replace(',', '.');
                    
                    LogMessage("alerts.log", "WARNING", 
                        $"Intentando parsear valor limpio: '{payload}' -> '{cleanPayload}'");
                    
                    isValidNumber = double.TryParse(cleanPayload, 
                        System.Globalization.NumberStyles.Any, 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        out sensorValue);
                }
                
                if (isValidNumber)
                {
                    await ProcessValidSensorValue(topic, sensorValue, timestamp, greenhouseDirectory);
                }
                else
                {
                    // Intentar parsear el valor como un número entero
                    if (int.TryParse(payload, out int intValue))
                    {
                        sensorValue = intValue;
                        isValidNumber = true;
                        LogMessage("alerts.log", "INFO", 
                            $"Valor convertido como entero: '{payload}' -> {sensorValue}");
                    }
                    else
                    {
                        // Intentar parsear el valor como un número decimal
                        if (double.TryParse(payload, out double doubleValue))
                        {
                            sensorValue = doubleValue;
                            isValidNumber = true;
                            LogMessage("alerts.log", "INFO", 
                                $"Valor convertido como decimal: '{payload}' -> {sensorValue}");
                        }
                        else
                        {
                            // Intentar parsear el valor como un número hexadecimal
                            if (int.TryParse(payload, System.Globalization.NumberStyles.HexNumber, null, out int hexValue))
                            {
                                sensorValue = hexValue;
                                isValidNumber = true;
                                LogMessage("alerts.log", "INFO", 
                                    $"Valor convertido como hexadecimal: '{payload}' -> {sensorValue}");
                            }
                            
                            // Intentar parsear el valor como un número de duración
                            isValidNumber = double.TryParse(payload, 
                                System.Globalization.NumberStyles.Any, 
                                System.Globalization.CultureInfo.InvariantCulture, 
                                out sensorValue);
                            
                            if (isValidNumber)
                            {
                                await ProcessValidSensorValue(topic, sensorValue, timestamp, greenhouseDirectory);
                            }
                        }
                    }
                }
            }
            else
            {
                // Registrar el mensaje con formato inválido
                LogMessage("error.log", "WARNING", 
                    $"Recibido valor no numérico en topic {topic}: '{payload}', no se pudo convertir.");
            }
        }

        private async Task ProcessValidSensorValue(string topic, double sensorValue, DateTime timestamp, string greenhouseDirectory)
        {
            try
            {
                // Usar un timeout para la conexión a la base de datos
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString + ";Connection Timeout=5"))
                {
                    await connection.OpenAsync();
                    
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT s.SensorID, s.SensorName, s.Units, s.SensorType, g.UserID, s.Topic " +
                        "FROM Sensors s " +
                        "INNER JOIN GreenHouses g ON s.GreenHouseID = g.GreenHouseID " +
                        "WHERE s.Topic = @Topic",
                        connection
                    );
                    command.CommandTimeout = 5; // Establecer timeout de 5 segundos
                    command.Parameters.AddWithValue("@Topic", topic);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var sensorId = Convert.ToInt32(reader["SensorID"]);
                            var sensorName = reader["SensorName"].ToString();
                            var units = reader["Units"].ToString();
                            var sensorType = Convert.ToInt32(reader["SensorType"]);
                            var userId = reader["UserID"].ToString();

                            // Guardar el valor original para análisis
                            double originalValue = sensorValue;
                            
                            // Comprobar si necesitamos actualizar la calibración del sensor
                            if (!sensorCalibrations.ContainsKey(sensorId))
                            {
                                // Primera vez que vemos este sensor, crear nueva calibración
                                sensorCalibrations[sensorId] = new SensorCalibrationInfo
                                {
                                    SensorID = sensorId,
                                    SensorType = sensorType,
                                    RecentValues = new List<double> { originalValue }
                                };
                            }
                            else
                            {
                                // Agregar valor a la lista de valores recientes
                                var calibration = sensorCalibrations[sensorId];
                                calibration.RecentValues.Add(originalValue);
                                if (calibration.RecentValues.Count > 10)
                                    calibration.RecentValues.RemoveAt(0);
                                
                                calibration.SampleCount++;
                                
                                // Cada 5 muestras, recalibrar
                                if (calibration.SampleCount % 5 == 0)
                                {
                                    // Actualizar calibración basado en los valores históricos
                                    UpdateSensorCalibration(calibration);
                                }
                            }
                            
                            // Aplicar la conversión según la calibración actual
                            sensorValue = ApplyCalibration(sensorValue, sensorCalibrations[sensorId]);
                            
                            LogMessage("alerts.log", "INFO", 
                                $"Sensor ID: {sensorId}, Nombre: {sensorName}, Tipo: {sensorType}, " +
                                $"Valor original: {originalValue}, Valor convertido: {sensorValue} {units}, " +
                                $"Factor: {sensorCalibrations[sensorId].ConversionFactor}");

                            var logEntry = new StringBuilder();
                            logEntry.Insert(0, "════════════════════════════════════════\n");
                            logEntry.Insert(0, $"Valor original: {originalValue}\n");
                            logEntry.Insert(0, $"Valor convertido: {sensorValue} {units}\n");
                            logEntry.Insert(0, $"Factor de conversión: {sensorCalibrations[sensorId].ConversionFactor}\n");
                            logEntry.Insert(0, $"Sensor: {sensorName}\n");
                            logEntry.Insert(0, $"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss.fff}\n");
                            logEntry.Insert(0, "════════════════════════════════════════\n");

                            var fileName = topic.Replace("/", "_") + ".log";
                            var logPath = Path.Combine(greenhouseDirectory, fileName);
                            PrependToFile(logPath, logEntry.ToString());
                            
                            // Verificar alertas para este sensor - solo si el valor es válido
                            try
                            {
                                await CheckAndCreateAlerts(topic, sensorValue);
                            }
                            catch (Exception ex)
                            {
                                LogMessage("error.log", "ERROR", $"Error al verificar alertas para {topic}: {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            // El sensor no está registrado en la base de datos
                            LogMessage("unknown.log", "WARNING", 
                                $"Mensaje recibido para un sensor no registrado - Topic: {topic}, Valor: {sensorValue}");
                            
                            // Intentar extraer el ID del invernadero del topic
                            string greenhouseId = "desconocido";
                            if (topic.Contains("/"))
                            {
                                greenhouseId = topic.Split('/')[0];
                            }
                            
                            // Registrar el mensaje en un archivo específico para este topic desconocido
                            var unknownFileName = "unknown_" + topic.Replace("/", "_") + ".log";
                            var unknownLogPath = Path.Combine(logDirectory, unknownFileName);
                            
                            var unknownEntry = new StringBuilder();
                            unknownEntry.Insert(0, "════════════════════════════════════════\n");
                            unknownEntry.Insert(0, $"Valor recibido: {sensorValue}\n");
                            unknownEntry.Insert(0, $"Topic: {topic}\n");
                            unknownEntry.Insert(0, $"Posible invernadero: {greenhouseId}\n");
                            unknownEntry.Insert(0, $"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss.fff}\n");
                            unknownEntry.Insert(0, "════════════════════════════════════════\n");
                            
                            PrependToFile(unknownLogPath, unknownEntry.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", $"Error al procesar valor para el topic {topic}: {ex.Message}", ex);
            }
        }

        private void UpdateSensorCalibration(SensorCalibrationInfo calibration)
        {
            if (calibration.RecentValues.Count < 3)
                return; // Necesitamos al menos 3 valores para hacer un análisis significativo
            
            // Analizar el rango típico según el tipo de sensor
            double minExpectedValue = -40; // Valor mínimo esperado para temperatura
            double maxExpectedValue = 125; // Valor máximo esperado para temperatura
            
            switch (calibration.SensorType)
            {
                case 0: // Temperatura
                    minExpectedValue = -40; // Mínimo típico para sensores de temperatura
                    maxExpectedValue = 125; // Máximo típico para sensores de temperatura
                    break;
                case 1: // Humedad
                    minExpectedValue = 0;   // Mínimo para humedad relativa
                    maxExpectedValue = 100; // Máximo para humedad relativa
                    break;
                case 2: // CO2
                    minExpectedValue = 400;  // Valor mínimo de CO2 en ambiente exterior
                    maxExpectedValue = 5000; // Valor máximo razonable en interiores
                    break;
                case 3: // Luminosidad
                    minExpectedValue = 0;      // Mínimo (oscuridad)
                    maxExpectedValue = 100000; // Máximo para luz solar directa
                    break;
            }
            
            // Calcular valor promedio de las muestras recientes
            double avgValue = calibration.RecentValues.Average();
            
            // Determinar el factor de conversión apropiado analizando el valor promedio
            if (avgValue > maxExpectedValue)
            {
                // Si el valor está fuera del rango esperado, intentar encontrar un factor
                // que lleve los valores dentro del rango esperado
                
                // Analizar cuántos dígitos tiene para determinar el factor más probable
                string valueStr = Math.Round(avgValue, 0).ToString();
                int digitCount = valueStr.Length;
                
                double testFactor = 1.0;
                
                if (calibration.SensorType == 0 || calibration.SensorType == 1) // Temperatura o humedad
                {
                    if (digitCount >= 4) // Probablemente milésimas (valor * 1000)
                    {
                        testFactor = 1000.0;
                    }
                    else if (digitCount == 3) // Probablemente centésimas (valor * 100)
                    {
                        testFactor = 100.0;
                    }
                    else if (digitCount == 2) // Probablemente décimas (valor * 10)
                    {
                        testFactor = 10.0;
                    }
                }
                
                // Verificar si el factor es apropiado
                double testValue = avgValue / testFactor;
                if (testValue >= minExpectedValue && testValue <= maxExpectedValue)
                {
                    calibration.ConversionFactor = testFactor;
                    calibration.IsCalibrated = true;
                    calibration.ValueFormat = GetValueFormat(testFactor);
                    
                    LogMessage("calibration.log", "INFO", 
                        $"Sensor {calibration.SensorID} calibrado: Valor medio {avgValue}, Factor {testFactor}, " +
                        $"Resultado {testValue}, Formato: x{testFactor}");
                }
            }
            else if (!calibration.IsCalibrated)
            {
                // Si los valores están dentro del rango esperado y no hay calibración previa
                calibration.ConversionFactor = 1.0;
                calibration.IsCalibrated = true;
                calibration.ValueFormat = 0;
                
                LogMessage("calibration.log", "INFO", 
                    $"Sensor {calibration.SensorID} no requiere calibración: Valores dentro del rango esperado {minExpectedValue}-{maxExpectedValue}");
            }
        }

        private int GetValueFormat(double factor)
        {
            if (factor == 10.0) return 1;
            if (factor == 100.0) return 2;
            if (factor == 1000.0) return 3;
            return 0;
        }

        private double ApplyCalibration(double value, SensorCalibrationInfo calibration)
        {
            if (!calibration.IsCalibrated)
            {
                // Si aún no hay calibración, aplicar la conversión inteligente
                return NormalizeSensorValue(value, calibration.SensorType);
            }
            
            // Aplicar el factor de conversión y redondear según el tipo de sensor
            double result = value / calibration.ConversionFactor;
            
            switch (calibration.SensorType)
            {
                case 0: // Temperatura
                    return Math.Round(result, 1); // 1 decimal para temperatura
                    
                case 1: // Humedad
                    return Math.Round(result, 0); // Sin decimales para humedad
                    
                case 2: // CO2
                    return Math.Round(result, 0); // Sin decimales para CO2
                    
                case 3: // Luminosidad
                    return Math.Round(result, 0); // Sin decimales para luminosidad
                    
                default:
                    return Math.Round(result, 2); // 2 decimales por defecto
            }
        }

        // Método para normalizar el valor del sensor según su tipo
        private double NormalizeSensorValue(double value, int sensorType)
        {
            switch (sensorType)
            {
                case 0: // Temperatura
                    // Para sensores de temperatura, analizar el rango y formato esperado
                    // Muchos sensores DHT o DS18B20 envían datos en milésimas de grado (valor entero * 1000)
                    // Por ejemplo, 25.6°C se envía como 25600
                    if (value > 100)
                    {
                        // Analizar cuántos dígitos tiene el número para determinar el factor de escala
                        string valueStr = value.ToString("F0");
                        int digitCount = valueStr.Length;
                        
                        LogMessage("alerts.log", "DEBUG", 
                            $"Valor temperatura: {value}, Dígitos: {digitCount} - Analizando formato");
                        
                        if (digitCount >= 4 && digitCount <= 5) // Valores como 1234 o 12345
                        {
                            // Probablemente sea un valor en milésimas de grado (formato típico de sensores DHT22, DS18B20)
                            // o en centésimas de grado (algunos sensores industriales)
                            // Valores esperados en Celsius: generalmente entre -40°C y +125°C (típico para DHT22, DS18B20)
                            
                            double normalizedValue = value / 1000.0; // Primero intentar milésimas
                            
                            // Si el valor normalizado está fuera del rango típico, probar con centésimas
                            if (normalizedValue < -40 || normalizedValue > 125)
                            {
                                normalizedValue = value / 100.0;
                            }
                            
                            // Si sigue fuera de rango, podría ser en décimas
                            if (normalizedValue < -40 || normalizedValue > 125)
                            {
                                normalizedValue = value / 10.0;
                            }
                            
                            LogMessage("alerts.log", "INFO", 
                                $"Convertido valor temperatura de {value} a {normalizedValue:F2}°C");
                            
                            return Math.Round(normalizedValue, 1);
                        }
                        else if (digitCount > 5) // Valores muy grandes, posiblemente otro formato o error
                        {
                            LogMessage("alerts.log", "WARNING", 
                                $"Valor de temperatura inusualmente alto: {value}, aplicando conversión experimental");
                            
                            // Intentar identificar un patrón o obtener un valor razonable
                            // Para este caso, usaremos una aproximación basada en el análisis de bytes recibidos
                            return Math.Round(value % 100, 1); // Como último recurso, extraer los 2 últimos dígitos
                        }
                    }
                    return value; // Si está dentro del rango normal, devolverlo tal cual
                
                case 1: // Humedad
                    // Los sensores de humedad suelen enviar datos en décimas o centésimas de porcentaje
                    // Por ejemplo, 45.7% se envía como 457 o 4570
                    if (value > 100)
                    {
                        string valueStr = value.ToString("F0");
                        int digitCount = valueStr.Length;
                        
                        if (digitCount == 3) // Valores como 457 (45.7%)
                        {
                            return Math.Round(value / 10.0, 1);
                        }
                        else if (digitCount >= 4) // Valores como 4570 (45.7%)
                        {
                            return Math.Round(value / 100.0, 1);
                        }
                    }
                    return Math.Min(100, value); // La humedad relativa nunca debe superar el 100%
                
                case 2: // CO2
                    // Los valores de CO2 suelen estar en PPM (partes por millón)
                    // Rango típico: 400 PPM (aire fresco) hasta 5000 PPM (muy contaminado)
                    if (value > 10000) // Valor muy alto para CO2
                    {
                        string valueStr = value.ToString("F0");
                        int digitCount = valueStr.Length;
                        
                        if (digitCount >= 5) // Posiblemente en formato incorrecto (x10 o x100)
                        {
                            double normalizedValue = value / 100.0;
                            if (normalizedValue > 400 && normalizedValue < 5000)
                            {
                                return Math.Round(normalizedValue, 0);
                            }
                            else
                            {
                                return Math.Round(value / 10.0, 0);
                            }
                        }
                    }
                    return value; // Dentro de rango normal
                
                case 3: // Brillo/Luminosidad
                    // Luminosidad en lux puede tener un rango muy amplio (1-100000 lux)
                    return value;
                
                default:
                    return value;
            }
        }

        private async Task CheckAndCreateAlerts(string sensorTopic, double value)
        {
            try
            {
                // Si ha pasado el intervalo de refresco, actualizar configuraciones
                if ((DateTime.Now - lastConfigRefresh).TotalMinutes >= CONFIG_REFRESH_INTERVAL_MINUTES)
                {
                    await RefreshConfigurations();
                }
                
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Obtener información del sensor
                    var sensorCommand = new System.Data.SqlClient.SqlCommand(
                        "SELECT s.SensorID, s.GreenHouseID, s.SensorType, s.SensorName, s.Units, g.UserID " +
                        "FROM Sensors s " +
                        "INNER JOIN GreenHouses g ON s.GreenHouseID = g.GreenHouseID " +
                        "WHERE s.Topic = @Topic",
                        connection
                    );
                    sensorCommand.Parameters.AddWithValue("@Topic", sensorTopic);
                    
                    int sensorId = 0;
                    string greenhouseId = "";
                    int sensorType = 0;
                    string sensorName = "";
                    string units = "";
                    string userId = "";
                    
                    using (var reader = await sensorCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            sensorId = Convert.ToInt32(reader["SensorID"]);
                            greenhouseId = reader["GreenHouseID"].ToString();
                            sensorType = Convert.ToInt32(reader["SensorType"]);
                            sensorName = reader["SensorName"].ToString();
                            units = reader["Units"].ToString();
                            userId = reader["UserID"].ToString();
                            
                            LogMessage("alerts.log", "INFO", 
                                $"Sensor encontrado para verificar alertas - ID: {sensorId}, Nombre: {sensorName}, " +
                                $"Tipo: {sensorType}, Usuario: {userId}, Valor a verificar: {value} {units}");
                        }
                        else
                        {
                            // Sensor no encontrado
                            LogMessage("error.log", "ERROR", $"Sensor no encontrado para el topic: {sensorTopic}");
                            return;
                        }
                    }
                    
                    // Validar que el valor esté dentro de un rango razonable según el tipo de sensor
                    if (!IsValidSensorValue(sensorType, value))
                    {
                        LogMessage("error.log", "WARNING", 
                            $"Valor del sensor fuera de rango razonable: {value} para sensor tipo {sensorType}, topic: {sensorTopic}");
                        return; // No procesamos valores anómalos
                    }
                    
                    // IMPORTANTE: Verificar explícitamente si existe una alerta en la base de datos para este sensor
                    var alertExistsCommand = new System.Data.SqlClient.SqlCommand(
                        "SELECT COUNT(*) FROM Alerts " +
                        "WHERE SensorID = @SensorID AND IsNotification = 0",
                        connection
                    );
                    alertExistsCommand.Parameters.AddWithValue("@SensorID", sensorId);
                    
                    int alertCount = (int)await alertExistsCommand.ExecuteScalarAsync();
                    if (alertCount == 0)
                    {
                        // No existe ninguna alerta configurada para este sensor en la BD
                        LogMessage("alerts.log", "INFO", 
                            $"No hay configuración de alerta en la BD para el sensor ID: {sensorId}, omitiendo verificación");
                        
                        // Si hay una configuración en caché para este sensor, eliminarla
                        if (alertConfigurations.ContainsKey(sensorId))
                        {
                            alertConfigurations.Remove(sensorId);
                            LogMessage("alerts.log", "INFO", 
                                $"Eliminada configuración de alerta en caché para sensor ID: {sensorId} (no existe en BD)");
                        }
                        
                        return;
                    }
                    
                    // Verificar si hay configuración de alerta para este sensor en nuestro cache
                    AlertConfigurationInfo alertConfig = null;
                    if (alertConfigurations.TryGetValue(sensorId, out alertConfig))
                    {
                        LogMessage("alerts.log", "INFO", 
                            $"Usando configuración en cache para sensor ID: {sensorId}, Rango: {alertConfig.ThresholdRange}");
                        
                        // IMPORTANTE: Verificar si la alerta en caché sigue existiendo en la BD
                        var alertStillExistsCommand = new System.Data.SqlClient.SqlCommand(
                            "SELECT COUNT(*) FROM Alerts " +
                            "WHERE AlertID = @AlertID AND IsNotification = 0",
                            connection
                        );
                        alertStillExistsCommand.Parameters.AddWithValue("@AlertID", alertConfig.AlertID);
                        
                        int alertStillExists = (int)await alertStillExistsCommand.ExecuteScalarAsync();
                        if (alertStillExists == 0)
                        {
                            // La alerta en caché ya no existe, eliminarla del caché
                            alertConfigurations.Remove(sensorId);
                            LogMessage("alerts.log", "INFO", 
                                $"Alerta en caché (ID: {alertConfig.AlertID}) ya no existe en la BD, eliminada del caché");
                            
                            // Verificar si hay una nueva alerta para este sensor
                            var newAlertCommand = new System.Data.SqlClient.SqlCommand(
                                "SELECT TOP 1 AlertID, ThresholdRange, NotifyByEmail, NotifyByPush, Severity " +
                                "FROM Alerts " +
                                "WHERE SensorID = @SensorID AND IsNotification = 0 " +
                                "ORDER BY CreatedAt DESC",
                                connection
                            );
                            newAlertCommand.Parameters.AddWithValue("@SensorID", sensorId);
                            
                            using (var reader = await newAlertCommand.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    alertConfig = new AlertConfigurationInfo
                                    {
                                        AlertID = Convert.ToInt32(reader["AlertID"]),
                                        SensorID = sensorId,
                                        ThresholdRange = reader["ThresholdRange"].ToString(),
                                        NotifyByEmail = Convert.ToBoolean(reader["NotifyByEmail"]),
                                        NotifyByPush = Convert.ToBoolean(reader["NotifyByPush"]),
                                        Severity = Convert.ToInt32(reader["Severity"]),
                                        CreatedAt = DateTime.Now
                                    };
                                    
                                    alertConfigurations[sensorId] = alertConfig;
                                    
                                    LogMessage("alerts.log", "INFO", 
                                        $"Nueva configuración de alerta cargada de BD - ID: {alertConfig.AlertID}, " +
                                        $"Rango: {alertConfig.ThresholdRange}");
                                }
                                else
                                {
                                    // No hay configuración de alerta para este sensor
                                    LogMessage("alerts.log", "INFO", $"No hay configuración de alerta para el sensor ID: {sensorId}");
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Si no está en cache, intentar recuperarla de la BD
                        var alertCommand = new System.Data.SqlClient.SqlCommand(
                            "SELECT AlertID, ThresholdRange, NotifyByEmail, NotifyByPush, Severity " +
                            "FROM Alerts " +
                            "WHERE SensorID = @SensorID AND IsNotification = 0 " +
                            "ORDER BY CreatedAt DESC",
                            connection
                        );
                        alertCommand.Parameters.AddWithValue("@SensorID", sensorId);
                        
                        using (var reader = await alertCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int alertConfigId = Convert.ToInt32(reader["AlertID"]);
                                string thresholdRange = reader["ThresholdRange"].ToString();
                                bool notifyByEmail = Convert.ToBoolean(reader["NotifyByEmail"]);
                                bool notifyByPush = Convert.ToBoolean(reader["NotifyByPush"]);
                                int severityValue = Convert.ToInt32(reader["Severity"]);
                                
                                // Crear y almacenar la configuración
                                alertConfig = new AlertConfigurationInfo
                                {
                                    AlertID = alertConfigId,
                                    SensorID = sensorId,
                                    ThresholdRange = thresholdRange,
                                    NotifyByEmail = notifyByEmail,
                                    NotifyByPush = notifyByPush,
                                    Severity = severityValue,
                                    CreatedAt = DateTime.Now
                                };
                                
                                alertConfigurations[sensorId] = alertConfig;
                                
                                LogMessage("alerts.log", "INFO", 
                                    $"Configuración de alerta cargada de BD - ID: {alertConfigId}, " +
                                    $"Rango: {thresholdRange}, Email: {notifyByEmail}, Push: {notifyByPush}, " +
                                    $"Severidad: {severityValue}");
                            }
                            else
                            {
                                // No hay configuración de alerta para este sensor
                                LogMessage("alerts.log", "INFO", $"No hay configuración de alerta para el sensor ID: {sensorId}");
                                return;
                            }
                        }
                    }
                    
                    // Obtener el rango mínimo y máximo del umbral
                    double min = double.MinValue, max = double.MaxValue;
                    if (!string.IsNullOrEmpty(alertConfig.ThresholdRange) && alertConfig.ThresholdRange.Contains("-"))
                    {
                        var partes = alertConfig.ThresholdRange.Split('-');
                        if (partes.Length == 2)
                        {
                            if (!double.TryParse(partes[0].Trim(), out min))
                            {
                                LogMessage("error.log", "ERROR", $"Error al parsear el valor mínimo: {partes[0]}");
                                min = double.MinValue;
                            }
                            if (!double.TryParse(partes[1].Trim(), out max))
                            {
                                LogMessage("error.log", "ERROR", $"Error al parsear el valor máximo: {partes[1]}");
                                max = double.MaxValue;
                            }
                        }
                    }
                    
                    LogMessage("alerts.log", "INFO", 
                        $"Verificando valor {value} contra rango {min}-{max} para sensor ID: {sensorId}");
                    
                    // Verificar si el valor está FUERA del rango permitido
                    if (value < min || value > max)
                    {
                        LogMessage("alerts.log", "WARNING", 
                            $"VALOR FUERA DE RANGO - Sensor ID: {sensorId}, Nombre: {sensorName}, " +
                            $"Valor: {value}, Rango: {min}-{max}, Min: {min}, Max: {max}");
                        
                        // IMPORTANTE: Verificar si ya existe una notificación activa para este sensor
                        var existingNotificationCommand = new System.Data.SqlClient.SqlCommand(
                            "SELECT COUNT(*) FROM Alerts " +
                            "WHERE SensorID = @SensorID AND IsNotification = 1 AND IsResolved = 0",
                            connection
                        );
                        existingNotificationCommand.Parameters.AddWithValue("@SensorID", sensorId);
                        
                        int existingNotificationCount = (int)await existingNotificationCommand.ExecuteScalarAsync();
                        
                        if (existingNotificationCount > 0)
                        {
                            // Ya existe una notificación activa para este sensor, actualizar el valor actual
                            LogMessage("alerts.log", "INFO", 
                                $"Ya existe una notificación activa para el sensor ID: {sensorId}, actualizando el valor actual");
                                
                            var updateCommand = new System.Data.SqlClient.SqlCommand(
                                "UPDATE Alerts SET CurrentValue = @CurrentValue " +
                                "WHERE SensorID = @SensorID AND IsNotification = 1 AND IsResolved = 0",
                                connection
                            );
                            updateCommand.Parameters.AddWithValue("@CurrentValue", value);
                            updateCommand.Parameters.AddWithValue("@SensorID", sensorId);
                            
                            int rowsUpdated = await updateCommand.ExecuteNonQueryAsync();
                            
                            LogMessage("alerts.log", "INFO", 
                                $"Actualizada notificación existente para sensor ID: {sensorId}, " +
                                $"nuevo valor: {value}, filas actualizadas: {rowsUpdated}");
                            
                            return; // No crear una nueva notificación
                        }
                        
                        // Obtener la severidad de la configuración en caché
                        AlertSeverity severity;
                        if (alertConfig != null && alertConfig.Severity >= 0 && alertConfig.Severity <= 3)
                        {
                            severity = (AlertSeverity)alertConfig.Severity;
                            LogMessage("alerts.log", "INFO", 
                                $"Usando severidad de la caché: {severity} (valor entero: {alertConfig.Severity}) para sensor {sensorId}");
                        }
                        else
                        {
                            // Si no hay severidad válida en la configuración, usar Medium como valor por defecto
                            severity = AlertSeverity.Medium;
                            LogMessage("alerts.log", "INFO", 
                                $"Usando severidad por defecto: {severity} para sensor {sensorId}");
                        }
                        
                        // Generar mensaje de alerta
                        string message = GenerateAlertMessage(sensorType, value, min, max);
                        
                        LogMessage("alerts.log", "WARNING", 
                            $"ALERTA GENERADA - Sensor: {sensorName}, " +
                            $"Tipo: {sensorType}, " +
                            $"Rango: {alertConfig.ThresholdRange}, " +
                            $"Valor actual: {value}, " +
                            $"Severidad: {severity}");
                        
                        // Crear la notificación en la base de datos
                        var insertCommand = new System.Data.SqlClient.SqlCommand(
                            "INSERT INTO Alerts (GreenHouseID, SensorID, AlertType, Severity, Message, " +
                            "CreatedAt, IsResolved, ThresholdRange, CurrentValue, NotifyByEmail, NotifyByPush, IsNotification) " +
                            "VALUES (@GreenHouseID, @SensorID, @AlertType, @Severity, @Message, " +
                            "@CreatedAt, @IsResolved, @ThresholdRange, @CurrentValue, @NotifyByEmail, @NotifyByPush, @IsNotification)",
                            connection
                        );
                        
                        insertCommand.Parameters.AddWithValue("@GreenHouseID", greenhouseId);
                        insertCommand.Parameters.AddWithValue("@SensorID", sensorId);
                        // Mapeo correcto de SensorType a AlertType
                        int alertType;
                        switch (sensorType)
                        {
                            case 0: // Temperature
                                alertType = 0; // AlertType.Temperature
                                break;
                            case 1: // CO2
                                alertType = 2; // AlertType.CO2
                                break;
                            case 2: // Brightness
                                alertType = 3; // AlertType.Brightness
                                break;
                            case 3: // Humidity
                                alertType = 1; // AlertType.Humidity
                                break;
                            default:
                                alertType = 0; // AlertType.Temperature por defecto
                                break;
                        }
                        insertCommand.Parameters.AddWithValue("@AlertType", alertType);
                        insertCommand.Parameters.AddWithValue("@Severity", (int)severity);
                        insertCommand.Parameters.AddWithValue("@Message", message);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                        insertCommand.Parameters.AddWithValue("@IsResolved", false);
                        insertCommand.Parameters.AddWithValue("@ThresholdRange", alertConfig.ThresholdRange);
                        insertCommand.Parameters.AddWithValue("@CurrentValue", value);
                        insertCommand.Parameters.AddWithValue("@NotifyByEmail", alertConfig.NotifyByEmail); // Usar la configuración del usuario
                        insertCommand.Parameters.AddWithValue("@NotifyByPush", alertConfig.NotifyByPush);  // Usar la configuración del usuario
                        insertCommand.Parameters.AddWithValue("@IsNotification", true);
                        
                        int rowsAffected = await insertCommand.ExecuteNonQueryAsync();
                        
                        LogMessage("alerts.log", "INFO", 
                            $"Alerta creada en BD - Sensor: {sensorName}, " +
                            $"valor: {value} {units}, umbral: {alertConfig.ThresholdRange}, " +
                            $"filas afectadas: {rowsAffected}");
                        
                        // Verificar si se debe enviar notificación por correo
                        if (alertConfig.NotifyByEmail)
                        {
                            // Aquí iría la lógica para enviar correo
                            LogMessage("alerts.log", "INFO", 
                                $"Se debería enviar correo para alerta de sensor {sensorId} - {sensorName}");
                        }
                        
                        // Verificar si se debe enviar notificación push
                        if (alertConfig.NotifyByPush)
                        {
                            // Aquí iría la lógica para enviar notificación push
                            LogMessage("alerts.log", "INFO", 
                                $"Se debería enviar notificación push para alerta de sensor {sensorId} - {sensorName}");
                        }
                    }
                    else
                    {
                        // Si el valor está dentro del rango, NO resolver notificaciones automáticamente
                        // Simplemente registrar en el log que el valor volvió a estar dentro del rango
                        LogMessage("alerts.log", "INFO", 
                            $"El valor {value} está dentro del rango permitido {min}-{max} para sensor ID: {sensorId}, " +
                            $"pero la notificación debe ser resuelta manualmente");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al verificar alertas", ex);
            }
        }

        private bool IsValidSensorValue(int sensorType, double value)
        {
            switch (sensorType)
            {
                case 0: // Temperatura
                    return value >= -50 && value <= 150; // Rango amplio para sensores de temperatura
                    
                case 1: // CO2
                    return value >= 0 && value <= 10000; // CO2 en ppm, hasta 10000 ppm
                    
                case 2: // Luminosidad
                    return value >= 0 && value <= 150000; // Luminosidad en lux, hasta 150000 lux (luz solar directa)
                    
                case 3: // Humedad
                    return value >= 0 && value <= 100; // Humedad relativa entre 0% y 100%
                    
                default:
                    return true; // Para tipos desconocidos, aceptar cualquier valor
            }
        }

        private AlertSeverity DetermineSeverity(double value, double min, double max, SensorType sensorType)
        {
            // Calcular qué tan lejos está el valor del rango permitido
            double rangeSize = max - min;
            double deviation = 0;
            
            if (value < min)
                deviation = (min - value) / rangeSize;
            else if (value > max)
                deviation = (value - max) / rangeSize;
                
            if (deviation > 0.5)
                return AlertSeverity.Critical;
            else if (deviation > 0.3)
                return AlertSeverity.High;
            else if (deviation > 0.1)
                return AlertSeverity.Medium;
            else
                return AlertSeverity.Low;
        }

        private string GenerateAlertMessage(int sensorType, double value, double min, double max)
        {
            string sensorName = GetSensorTypeName(sensorType);
            string unit = GetUnitForSensorType(sensorType);
            string formattedValue = FormatSensorValue(sensorType, value);
            string formattedMin = FormatSensorValue(sensorType, min);
            string formattedMax = FormatSensorValue(sensorType, max);
            
            if (value < min)
            {
                return $"El valor de {sensorName} ({formattedValue}{unit}) está por debajo del mínimo permitido ({formattedMin}{unit}).";
            }
            else
            {
                return $"El valor de {sensorName} ({formattedValue}{unit}) está por encima del máximo permitido ({formattedMax}{unit}).";
            }
        }

        private string FormatSensorValue(int sensorType, double value)
        {
            switch (sensorType)
            {
                case 0: // Temperatura
                    return value.ToString("F1"); // 1 decimal para temperatura
                    
                case 1: // CO2
                    return value.ToString("F0"); // Sin decimales para CO2
                    
                case 2: // Luminosidad
                    return value.ToString("F0"); // Sin decimales para luminosidad
                    
                case 3: // Humedad
                    return value.ToString("F0"); // Sin decimales para humedad
                    
                default:
                    return value.ToString("F2"); // 2 decimales por defecto
            }
        }

        private string GetSensorTypeName(int sensorType)
        {
            switch (sensorType)
            {
                case 0: // Temperature
                    return "temperatura";
                case 1: // CO2
                    return "CO2";
                case 2: // Brightness
                    return "luminosidad";
                case 3: // Humidity
                    return "humedad";
                default:
                    return "sensor";
            }
        }

        private string GetUnitForSensorType(int sensorType)
        {
            switch (sensorType)
            {
                case 0: // Temperatura
                    return "°C";
                case 1: // CO2
                    return "ppm";
                case 2: // Luminosidad
                    return "lux";
                case 3: // Humidity
                    return "%";
                default:
                    return "";
            }
        }

        private void StartInactivityTimer(string topic)
        {
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(INACTIVITY_THRESHOLD_SECONDS));
                
                if (lastMessageTimes.TryGetValue(topic, out DateTime lastTime))
                {
                    var timeSinceLastMessage = DateTime.Now - lastTime;
                    
                    if (timeSinceLastMessage.TotalSeconds >= INACTIVITY_THRESHOLD_SECONDS)
                    {
                        var endSessionMessage = new StringBuilder();
                        endSessionMessage.Insert(0, "════════════════════════════════════════\n");
                        endSessionMessage.Insert(0, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FIN DE RECEPCIÓN DE DATOS\n");
                        endSessionMessage.Insert(0, $"Último mensaje recibido hace: {timeSinceLastMessage.TotalSeconds:F1} segundos\n");
                        endSessionMessage.Insert(0, "════════════════════════════════════════\n");

                        var greenhouseDirectory = GetGreenHouseDirectory(topic);
                        var fileName = topic.Replace("/", "_") + ".log";
                        var logPath = Path.Combine(greenhouseDirectory, fileName);
                        PrependToFile(logPath, endSessionMessage.ToString());

                        lastMessageTimes.Remove(topic);
                    }
                }
            });
        }

        private async Task StartMQTTClient()
        {
            try
            {
                var mqttFactory = new MqttFactory();
                mqttClient = mqttFactory.CreateMqttClient();

                // Opciones básicas de conexión
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("broker.hivemq.com", 1883)
                    .WithClientId($"GreenHouseLogger_{Guid.NewGuid()}")
                    .WithCleanSession(true)
                    .Build();

                // Configurar manejador para los mensajes recibidos
                mqttClient.UseApplicationMessageReceivedHandler(async e =>
                {
                    await HandleMqttMessage(e);
                });

                // Configurar manejador para la desconexión
                mqttClient.UseDisconnectedHandler(async e =>
                {
                    LogMessage("service.log", "WARNING", "Desconexión del broker MQTT detectada. Intentando reconectar...");
                    
                    // Esperar 5 segundos antes de intentar reconectar
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    
                    try
                    {
                        // Intentar reconectar
                        await mqttClient.ConnectAsync(options);
                        LogMessage("service.log", "INFO", "Reconexión al broker MQTT exitosa");
                        
                        // Suscribirse nuevamente a los topics
                        await SubscribeToSensorTopics();
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error.log", "ERROR", $"Error al intentar reconectar al broker MQTT: {ex.Message}", ex);
                    }
                });

                // Conectar al broker MQTT con reintentos
                int maxRetries = 3;
                int retryCount = 0;
                bool connected = false;

                while (!connected && retryCount < maxRetries)
                {
                    try
                    {
                        await mqttClient.ConnectAsync(options);
                        connected = true;
                        LogMessage("service.log", "INFO", "Conectado al broker MQTT exitosamente");
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        LogMessage("error.log", "WARNING", 
                            $"Intento {retryCount}/{maxRetries} de conexión al broker MQTT falló: {ex.Message}");
                        
                        if (retryCount < maxRetries)
                        {
                            // Esperar antes del siguiente intento
                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                        else
                        {
                            throw; // Propagar la excepción si se agotan los reintentos
                        }
                    }
                }

                // Una vez conectado, suscribirse a los topics de los sensores
                await SubscribeToSensorTopics();
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", $"Error en la conexión MQTT: {ex.Message}", ex);
                
                // Esperar antes de intentar reiniciar
                await Task.Delay(TimeSpan.FromSeconds(10));
                
                // Reintentar la conexión
                await StartMQTTClient();
            }
        }

        // Método simplificado para suscribirse a los topics de los sensores
        private async Task SubscribeToSensorTopics()
        {
            if (mqttClient == null || !mqttClient.IsConnected)
            {
                LogMessage("error.log", "ERROR", "No se puede suscribir a temas: cliente MQTT no conectado");
                return;
            }

            try
            {
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT Topic FROM Sensors",
                        connection
                    );
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        int topicsSubscribed = 0;
                        
                        while (await reader.ReadAsync())
                        {
                            string topic = reader["Topic"].ToString();
                            
                            if (!string.IsNullOrWhiteSpace(topic))
                            {
                                try
                                {
                                    // Usar el método más simple de suscripción
                                    await mqttClient.SubscribeAsync(topic);
                                    topicsSubscribed++;
                                    LogMessage("service.log", "INFO", $"Suscrito al topic: {topic}");
                                }
                                catch (Exception ex)
                                {
                                    LogMessage("error.log", "WARNING", $"Error al suscribirse al topic {topic}: {ex.Message}");
                                }
                            }
                        }
                        
                        LogMessage("service.log", "INFO", $"Total suscripciones realizadas: {topicsSubscribed}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", $"Error al suscribirse a los topics: {ex.Message}", ex);
            }
        }
        
        // Mantenemos este método como un placeholder vacío por compatibilidad
        private async Task SubscribeToWildcardTopic()
        {
            // Método vacío que no hace nada
            await Task.CompletedTask;
        }

        private async Task LogDatabaseStatus()
        {
            try
            {
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    LogMessage("service.log", "INFO", "Verificando conexión con la base de datos...");
                    await connection.OpenAsync();
                    
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT COUNT(*) FROM Sensors",
                        connection
                    );
                    
                    var sensorCount = (int)await command.ExecuteScalarAsync();
                    LogMessage("service.log", "INFO", $"Base de datos conectada. {sensorCount} sensores encontrados.");
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al conectar con la base de datos", ex);
            }
        }

        // Método para normalizar separadores decimales
        private string NormalizeDecimalSeparator(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Eliminar espacios en blanco y caracteres no imprimibles
            input = input.Trim();
            
            // Analizar si el input contiene caracteres no válidos para un número
            bool hasNonNumericChars = input.Any(c => !char.IsDigit(c) && c != '.' && c != ',' && c != '-' && c != '+' && c != 'e' && c != 'E');
            if (hasNonNumericChars)
            {
                LogMessage("debug.log", "WARNING", $"El valor contiene caracteres no numéricos: '{input}'");
                
                // Intentar extraer la parte numérica
                var numericPart = new string(input.Where(c => 
                    char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+' || c == 'e' || c == 'E').ToArray());
                
                LogMessage("debug.log", "INFO", $"Parte numérica extraída: '{numericPart}'");
                input = numericPart;
            }
            
            // Verificar si hay puntos y comas en el mismo número (formatos mixtos)
            bool hasDot = input.Contains('.');
            bool hasComma = input.Contains(',');
            
            if (hasDot && hasComma)
            {
                // Si hay ambos separadores, asumir que la coma es el separador de miles
                // y el punto es el separador decimal (formato anglosajón)
                input = input.Replace(",", "");
                LogMessage("debug.log", "INFO", $"Formato mixto detectado, usando punto como separador decimal: '{input}'");
            }
            else if (hasComma && !hasDot)
            {
                // Si solo hay comas, asumimos que es el separador decimal
                input = input.Replace(",", ".");
                LogMessage("debug.log", "INFO", $"Coma detectada como separador decimal, convertida a punto: '{input}'");
            }
            
            return input;
        }

        private void StartConfigRefreshTimer()
        {
            // Reducir el intervalo de refresco a 1 segundo para detectar cambios más rápidamente
            const int CONFIG_REFRESH_INTERVAL_SECONDS = 1;
            
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // Verificar si existe el archivo de señalización
                        string signalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "refresh_alert_config.signal");
                        bool forceRefresh = false;
                        
                        if (File.Exists(signalPath))
                        {
                            try
                            {
                                // Leer la fecha del archivo
                                string signalContent = File.ReadAllText(signalPath);
                                DateTime signalTime;
                                
                                if (DateTime.TryParse(signalContent, out signalTime))
                                {
                                    // Si la señal es reciente (menos de 30 segundos), forzar refresco
                                    if ((DateTime.Now - signalTime).TotalSeconds < 30)
                                    {
                                        forceRefresh = true;
                                        LogMessage("service.log", "INFO", 
                                            $"Detectada señal de refresco de configuración: {signalTime}");
                                    }
                                }
                                
                                // Eliminar el archivo de señalización
                                File.Delete(signalPath);
                            }
                            catch (Exception ex)
                            {
                                LogMessage("error.log", "ERROR", 
                                    $"Error al procesar archivo de señalización: {ex.Message}", ex);
                            }
                        }
                        
                        // Si no hay señal de forzar refresco, esperar el intervalo definido
                        if (!forceRefresh)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(CONFIG_REFRESH_INTERVAL_SECONDS));
                        }
                        
                        // Usar un token de cancelación para evitar bloqueos
                        using (var cts = new System.Threading.CancellationTokenSource())
                        {
                            // Establecer un timeout para la operación de refresco
                            cts.CancelAfter(TimeSpan.FromSeconds(30));
                            
                            try
                            {
                                // Refrescar configuraciones con timeout
                                await Task.Run(async () => await RefreshConfigurations(), cts.Token);
                                
                                LogMessage("service.log", "INFO", 
                                    $"Configuraciones refrescadas: {alertConfigurations.Count} alertas, " +
                                    $"{sensorCalibrations.Count} sensores calibrados");
                            }
                            catch (System.Threading.Tasks.TaskCanceledException)
                            {
                                LogMessage("error.log", "WARNING", 
                                    "Timeout al refrescar configuraciones. La operación tomó demasiado tiempo.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Registrar el error pero continuar con el ciclo
                        LogMessage("error.log", "ERROR", "Error al refrescar configuraciones", ex);
                        
                        // Esperar un poco antes de continuar para evitar ciclos de error rápidos
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
            });
        }

        private async Task RefreshConfigurations()
        {
            try
            {
                lastConfigRefresh = DateTime.Now;
                
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    // Establecer un timeout para evitar bloqueos en la conexión
                    connection.ConnectionString += ";Connection Timeout=10";
                    
                    try
                    {
                        await connection.OpenAsync();
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error.log", "ERROR", "Error al conectar con la base de datos para refrescar configuraciones", ex);
                        return; // Salir del método si no podemos conectar
                    }
                    
                    // Ejecutar cada operación de manera independiente sin una transacción global
                    // para evitar errores de transacción
                    try
                    {
                        // Primero, verificar directamente si hay alertas que han sido eliminadas
                        await VerifyAndCleanupDeletedAlerts(connection);
                        
                        // Refrescar configuraciones de alertas
                        await RefreshAlertConfigurations(connection);
                        
                        // Limpiar calibraciones de sensores que ya no existen
                        await CleanupSensorCalibrations(connection);
                        
                        // Limpiar alertas huérfanas o notificaciones antiguas
                        await CleanupOrphanedAlerts(connection);
                        
                        LogMessage("service.log", "INFO", "Configuraciones refrescadas correctamente");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error.log", "ERROR", $"Error durante el refresco de configuraciones: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al refrescar configuraciones de alertas", ex);
                // No propagamos la excepción para que el servicio siga funcionando
            }
        }
        
        private async Task VerifyAndCleanupDeletedAlerts(System.Data.SqlClient.SqlConnection connection)
        {
            try
            {
                // Si no hay alertas en caché, no hay nada que limpiar
                if (alertConfigurations.Count == 0)
                    return;
                
                // Obtener todas las alertas activas en la base de datos
                var command = new System.Data.SqlClient.SqlCommand(
                    "SELECT AlertID, SensorID FROM Alerts WHERE IsNotification = 0",
                    connection
                );
                
                // Crear un diccionario para almacenar las alertas existentes en la BD
                var existingAlerts = new Dictionary<int, int>(); // SensorID -> AlertID
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int alertId = Convert.ToInt32(reader["AlertID"]);
                        int sensorId = Convert.ToInt32(reader["SensorID"]);
                        existingAlerts[sensorId] = alertId;
                    }
                }
                
                // Encontrar alertas en caché que ya no existen en la BD
                var alertsToRemove = new List<int>();
                
                foreach (var kvp in alertConfigurations)
                {
                    int sensorId = kvp.Key;
                    int cachedAlertId = kvp.Value.AlertID;
                    
                    // Verificar si el sensor ya no tiene alertas en la BD
                    if (!existingAlerts.ContainsKey(sensorId))
                    {
                        alertsToRemove.Add(sensorId);
                        LogMessage("alerts.log", "INFO", 
                            $"Sensor ID {sensorId} ya no tiene alertas configuradas en la BD (verificación directa)");
                    }
                    // Verificar si el ID de la alerta ha cambiado
                    else if (existingAlerts[sensorId] != cachedAlertId)
                    {
                        alertsToRemove.Add(sensorId);
                        LogMessage("alerts.log", "INFO", 
                            $"Alerta para sensor ID {sensorId} ha cambiado en la BD - ID anterior: {cachedAlertId}, nuevo: {existingAlerts[sensorId]} (verificación directa)");
                    }
                }
                
                // Eliminar las alertas que ya no existen
                foreach (var sensorId in alertsToRemove)
                {
                    alertConfigurations.Remove(sensorId);
                    LogMessage("alerts.log", "INFO", 
                        $"Eliminada configuración de alerta en caché para sensor ID: {sensorId} (verificación directa)");
                }
                
                // Verificar si hay notificaciones activas para alertas que ya no existen
                if (alertsToRemove.Count > 0)
                {
                    // Construir la lista de IDs de sensores para la consulta SQL
                    string sensorIdList = string.Join(",", alertsToRemove);
                    
                    if (!string.IsNullOrEmpty(sensorIdList))
                    {
                        // Marcar como resueltas las notificaciones para sensores que ya no tienen alertas configuradas
                        var resolveCommand = new System.Data.SqlClient.SqlCommand(
                            $"UPDATE Alerts SET IsResolved = 1, ResolvedAt = GETDATE() " +
                            $"WHERE SensorID IN ({sensorIdList}) AND IsNotification = 1 AND IsResolved = 0",
                            connection
                        );
                        
                        int rowsResolved = await resolveCommand.ExecuteNonQueryAsync();
                        if (rowsResolved > 0)
                        {
                            LogMessage("alerts.log", "INFO", 
                                $"Resueltas {rowsResolved} notificaciones para sensores sin alertas configuradas");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al verificar alertas eliminadas", ex);
            }
        }

        private async Task RefreshAlertConfigurations(System.Data.SqlClient.SqlConnection connection)
        {
            // Usar CommandBehavior.SequentialAccess para evitar caché en la consulta
            var command = new System.Data.SqlClient.SqlCommand(
                "SELECT a.AlertID, a.SensorID, a.ThresholdRange, a.NotifyByEmail, a.NotifyByPush, a.Severity, a.CreatedAt " +
                "FROM Alerts a WITH (NOLOCK) " +  // Usar NOLOCK para evitar bloqueos
                "WHERE a.IsNotification = 0 " + 
                "ORDER BY a.CreatedAt DESC", // Ordenar para obtener las más recientes primero
                connection
            );
            // Asignar la conexión al comando
            command.Connection = connection;
            command.CommandTimeout = 10; // Establecer un timeout corto para evitar bloqueos
            
            var tempConfigs = new Dictionary<int, AlertConfigurationInfo>();
            
            try
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int sensorId = Convert.ToInt32(reader["SensorID"]);
                        
                        // Si ya tenemos una configuración para este sensor, omitirla
                        // (ya que estamos ordenando por CreatedAt DESC, la primera es la más reciente)
                        if (tempConfigs.ContainsKey(sensorId))
                            continue;
                        
                        var config = new AlertConfigurationInfo
                        {
                            AlertID = Convert.ToInt32(reader["AlertID"]),
                            SensorID = sensorId,
                            ThresholdRange = reader["ThresholdRange"].ToString(),
                            NotifyByEmail = Convert.ToBoolean(reader["NotifyByEmail"]),
                            NotifyByPush = Convert.ToBoolean(reader["NotifyByPush"]),
                            Severity = Convert.ToInt32(reader["Severity"]),
                            CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                        };
                        
                        tempConfigs[sensorId] = config;
                    }
                }
                
                // Comparar con las configuraciones actuales para detectar cambios
                var newAlerts = tempConfigs.Keys.Except(alertConfigurations.Keys).ToList();
                var removedAlerts = alertConfigurations.Keys.Except(tempConfigs.Keys).ToList();
                var changedAlerts = tempConfigs.Keys.Intersect(alertConfigurations.Keys)
                    .Where(key => 
                        tempConfigs[key].ThresholdRange != alertConfigurations[key].ThresholdRange ||
                        tempConfigs[key].Severity != alertConfigurations[key].Severity)
                    .ToList();
                
                // Registrar los cambios
                foreach (var newAlertId in newAlerts)
                {
                    LogMessage("alerts.log", "INFO", 
                        $"Nueva configuración de alerta detectada para sensor ID: {newAlertId}, " +
                        $"Umbral: {tempConfigs[newAlertId].ThresholdRange}, " +
                        $"Severidad: {tempConfigs[newAlertId].Severity}");
                }
                
                foreach (var removedAlertId in removedAlerts)
                {
                    LogMessage("alerts.log", "INFO", 
                        $"Configuración de alerta eliminada para sensor ID: {removedAlertId}");
                }
                
                foreach (var changedAlertId in changedAlerts)
                {
                    LogMessage("alerts.log", "INFO", 
                        $"Configuración de alerta modificada para sensor ID: {changedAlertId}, " +
                        $"Umbral anterior: {alertConfigurations[changedAlertId].ThresholdRange} → Nuevo: {tempConfigs[changedAlertId].ThresholdRange}, " +
                        $"Severidad anterior: {alertConfigurations[changedAlertId].Severity} → Nueva: {tempConfigs[changedAlertId].Severity}");
                }
                
                // Actualizar las configuraciones - Reemplazar completamente el diccionario
                alertConfigurations = new Dictionary<int, AlertConfigurationInfo>(tempConfigs);
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", $"Error al refrescar configuraciones de alertas: {ex.Message}", ex);
                throw; // Propagar la excepción para manejarla en el nivel superior
            }
        }

        private async Task CleanupSensorCalibrations(System.Data.SqlClient.SqlConnection connection)
        {
            var command = new System.Data.SqlClient.SqlCommand(
                "SELECT SensorID FROM Sensors",
                connection
            );
            // Asignar la conexión al comando
            command.Connection = connection;
            
            var existingSensorIds = new HashSet<int>();
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingSensorIds.Add(Convert.ToInt32(reader["SensorID"]));
                }
            }
            
            // Eliminar calibraciones para sensores que ya no existen
            var sensorsToRemove = sensorCalibrations.Keys.Where(id => !existingSensorIds.Contains(id)).ToList();
            foreach (var sensorId in sensorsToRemove)
            {
                sensorCalibrations.Remove(sensorId);
                LogMessage("calibration.log", "INFO", 
                    $"Eliminada calibración para sensor ID: {sensorId} (sensor no encontrado en la base de datos)");
            }
        }

        private async Task CleanupOrphanedAlerts(System.Data.SqlClient.SqlConnection connection)
        {
            try
            {
                // Eliminar alertas huérfanas (donde el sensor ya no existe)
                var orphanedCommand = new System.Data.SqlClient.SqlCommand(
                    "DELETE FROM Alerts " +
                    "WHERE SensorID NOT IN (SELECT SensorID FROM Sensors)",
                    connection
                );
                
                int orphanedDeleted = await orphanedCommand.ExecuteNonQueryAsync();
                if (orphanedDeleted > 0)
                {
                    LogMessage("alerts.log", "INFO", 
                        $"Eliminadas {orphanedDeleted} alertas huérfanas (sensores no existentes)");
                }
                
                // Marcar como resueltas las notificaciones antiguas (más de 7 días)
                var oldNotificationsCommand = new System.Data.SqlClient.SqlCommand(
                    "UPDATE Alerts " +
                    "SET IsResolved = 1, ResolvedAt = GETDATE() " +
                    "WHERE IsNotification = 1 AND IsResolved = 0 AND CreatedAt < DATEADD(day, -7, GETDATE())",
                    connection
                );
                
                int oldResolved = await oldNotificationsCommand.ExecuteNonQueryAsync();
                if (oldResolved > 0)
                {
                    LogMessage("alerts.log", "INFO", 
                        $"Marcadas como resueltas {oldResolved} notificaciones antiguas (más de 7 días)");
                }
                
                // Verificar si hay alertas en caché que ya no existen en la BD
                if (alertConfigurations.Count > 0)
                {
                    // Obtener todos los IDs de alertas que existen en la BD
                    var existingAlertsCommand = new System.Data.SqlClient.SqlCommand(
                        "SELECT AlertID, SensorID FROM Alerts WHERE IsNotification = 0",
                        connection
                    );
                    
                    var existingAlertIds = new HashSet<int>();
                    var existingSensorToAlertMap = new Dictionary<int, int>();
                    
                    using (var reader = await existingAlertsCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int alertId = Convert.ToInt32(reader["AlertID"]);
                            int sensorId = Convert.ToInt32(reader["SensorID"]);
                            
                            existingAlertIds.Add(alertId);
                            existingSensorToAlertMap[sensorId] = alertId;
                        }
                    }
                    
                    // Buscar configuraciones en caché cuyo AlertID ya no existe en la BD
                    var alertsToRemove = new List<int>();
                    foreach (var kvp in alertConfigurations)
                    {
                        int sensorId = kvp.Key;
                        int cachedAlertId = kvp.Value.AlertID;
                        
                        // Caso 1: El sensor ya no tiene ninguna alerta configurada
                        if (!existingSensorToAlertMap.ContainsKey(sensorId))
                        {
                            alertsToRemove.Add(sensorId);
                            LogMessage("alerts.log", "INFO", 
                                $"Sensor ID {sensorId} ya no tiene alertas configuradas en la BD");
                        }
                        // Caso 2: El sensor tiene una alerta configurada pero con un ID diferente (fue borrada y recreada)
                        else if (existingSensorToAlertMap[sensorId] != cachedAlertId)
                        {
                            alertsToRemove.Add(sensorId);
                            LogMessage("alerts.log", "INFO", 
                                $"Alerta para sensor ID {sensorId} ha cambiado en la BD (ID anterior: {cachedAlertId}, nuevo: {existingSensorToAlertMap[sensorId]})");
                        }
                    }
                    
                    // Eliminar las configuraciones que ya no existen o han cambiado
                    foreach (var sensorId in alertsToRemove)
                    {
                        alertConfigurations.Remove(sensorId);
                        LogMessage("alerts.log", "INFO", 
                            $"Eliminada configuración de alerta en caché para sensor ID: {sensorId} (alerta no encontrada o modificada en la BD)");
                    }
                    
                    // Verificar si hay nuevas alertas que no están en caché
                    foreach (var kvp in existingSensorToAlertMap)
                    {
                        int sensorId = kvp.Key;
                        int alertId = kvp.Value;
                        
                        if (!alertConfigurations.ContainsKey(sensorId) || 
                            alertConfigurations[sensorId].AlertID != alertId)
                        {
                            // Cargar la nueva configuración de alerta
                            var newAlertCommand = new System.Data.SqlClient.SqlCommand(
                                "SELECT AlertID, ThresholdRange, NotifyByEmail, NotifyByPush, Severity, CreatedAt " +
                                "FROM Alerts " +
                                "WHERE AlertID = @AlertID AND IsNotification = 0",
                                connection
                            );
                            newAlertCommand.Parameters.AddWithValue("@AlertID", alertId);
                            
                            using (var reader = await newAlertCommand.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    var config = new AlertConfigurationInfo
                                    {
                                        AlertID = alertId,
                                        SensorID = sensorId,
                                        ThresholdRange = reader["ThresholdRange"].ToString(),
                                        NotifyByEmail = Convert.ToBoolean(reader["NotifyByEmail"]),
                                        NotifyByPush = Convert.ToBoolean(reader["NotifyByPush"]),
                                        Severity = Convert.ToInt32(reader["Severity"]),
                                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                                    };
                                    
                                    alertConfigurations[sensorId] = config;
                                    
                                    LogMessage("alerts.log", "INFO", 
                                        $"Nueva configuración de alerta cargada en caché - Sensor ID: {sensorId}, " +
                                        $"Alerta ID: {alertId}, Umbral: {config.ThresholdRange}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al limpiar alertas huérfanas", ex);
            }
        }

        // Método para forzar la limpieza de una alerta específica del caché
        public void ForceAlertCleanupFromCache(int sensorId)
        {
            if (alertConfigurations.ContainsKey(sensorId))
            {
                alertConfigurations.Remove(sensorId);
                LogMessage("alerts.log", "INFO", 
                    $"Forzada eliminación de configuración de alerta en caché para sensor ID: {sensorId}");
            }
        }

        // Método estático para señalizar al servicio que debe refrescar las configuraciones
        public static void SignalConfigRefresh()
        {
            try
            {
                string appDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data");
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }

                string signalPath = Path.Combine(appDataPath, "refresh_alert_config.signal");
                File.WriteAllText(signalPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            catch (Exception ex)
            {
                // Registrar el error en el log de eventos de Windows
                using (var eventLog = new System.Diagnostics.EventLog("Application"))
                {
                    eventLog.Source = "MQTTLoggerService";
                    eventLog.WriteEntry($"Error al crear archivo de señalización: {ex.Message}", 
                                      System.Diagnostics.EventLogEntryType.Error);
                }
            }
        }
    }
}
