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

        protected override void OnStart(string[] args)
        {
            try 
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // Limpiar el diccionario al iniciar el servicio
                lastMessageTimes.Clear();
                
                LogMessage("service.log", "INFO", "Servicio iniciando...");
                
                Task.Run(async () => 
                {
                    await LogDatabaseStatus();
                    await StartMQTTClient();
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
                var greenhouseId = topic.Split('/')[0];
                var greenhousePath = Path.Combine(logDirectory, greenhouseId);
                
                // Crear el directorio si no existe
                if (!Directory.Exists(greenhousePath))
                {
                    Directory.CreateDirectory(greenhousePath);
                }
                
                return greenhousePath;
            }
            catch
            {
                // Si hay algún error, usar el directorio de logs por defecto
                return logDirectory;
            }
        }

        private async Task HandleMqttMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                var timestamp = DateTime.Now;

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

                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT s.SensorID, s.SensorName, s.Units, g.UserID, s.Topic " +
                        "FROM Sensors s " +
                        "INNER JOIN GreenHouses g ON s.GreenHouseID = g.GreenHouseID " +
                        "WHERE s.Topic = @Topic",
                        connection
                    );
                    command.Parameters.AddWithValue("@Topic", topic);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var sensorId = reader["SensorID"].ToString();
                            var sensorName = reader["SensorName"].ToString();
                            var units = reader["Units"].ToString();

                            var logEntry = new StringBuilder();
                            logEntry.Insert(0, "════════════════════════════════════════\n");
                            logEntry.Insert(0, $"Valor: {payload} {units}\n");
                            logEntry.Insert(0, $"Sensor: {sensorName}\n");
                            logEntry.Insert(0, $"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss.fff}\n");
                            logEntry.Insert(0, "════════════════════════════════════════\n");

                            var fileName = topic.Replace("/", "_") + ".log";
                            var logPath = Path.Combine(greenhouseDirectory, fileName);
                            PrependToFile(logPath, logEntry.ToString());
                        }
                    }
                }

                // Iniciar el temporizador de inactividad
                StartInactivityTimer(topic);
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error al procesar mensaje MQTT", ex);
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

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("broker.hivemq.com", 1883)
                    .WithClientId($"GreenHouseLogger_{Guid.NewGuid()}")
                    .Build();

                mqttClient.UseApplicationMessageReceivedHandler(async e =>
                {
                    await HandleMqttMessage(e);
                });

                await mqttClient.ConnectAsync(options);
                LogMessage("service.log", "INFO", "Conectado al broker MQTT");

                // Suscribirse a los topics
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT COUNT(*) FROM Sensors",
                        connection
                    );
                    
                    var sensorCount = (int)await command.ExecuteScalarAsync();
                    
                    // Suscribirse a todos los topics
                    command.CommandText = "SELECT Topic FROM Sensors";
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            await mqttClient.SubscribeAsync(reader["Topic"].ToString());
                        }
                    }
                    
                    LogMessage("service.log", "INFO", $"Suscrito a {sensorCount} topics MQTT");
                }
            }
            catch (Exception ex)
            {
                LogMessage("error.log", "ERROR", "Error en la conexión MQTT", ex);
                await Task.Delay(5000);
                await StartMQTTClient();
            }
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
    }
}
