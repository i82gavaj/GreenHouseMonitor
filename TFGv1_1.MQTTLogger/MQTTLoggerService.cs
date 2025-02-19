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

        public MQTTLoggerService()
        {
            InitializeComponent();
            this.ServiceName = "MQTTLoggerService";
            this.logDirectory = @"C:\Users\josem\Desktop\Universidad\Nueva carpeta\TFGv1_1\TFGv1_1\Logs";
            this.connectionString = @"Data Source=DELPRADO\SQLEXPRESS;Initial Catalog=aspnet-TFGv1_1-202501301107282;Integrated Security=True;TrustServerCertificate=True";
        }

        protected override void OnStart(string[] args)
        {
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(Path.Combine(logDirectory, "service.log"), "Servicio iniciando...\n");
            
            Task.Run(async () => 
            {
                await LogDatabaseStatus();
                await StartMQTTClient();
            });
        }

        protected override void OnStop()
        {
            if (mqttClient != null && mqttClient.IsConnected)
            {
                mqttClient.DisconnectAsync().Wait();
                File.AppendAllText(Path.Combine(logDirectory, "service.log"), "Servicio detenido\n");
            }
        }

        private void PrependToFile(string filePath, string content)
        {
            try
            {
                string existingContent = "";
                if (File.Exists(filePath))
                {
                    existingContent = File.ReadAllText(filePath);
                }
                File.WriteAllText(filePath, content + existingContent);
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(logDirectory, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Error escribiendo en {filePath}: {ex.Message}\n"
                );
            }
        }

        private async Task StartMQTTClient()
        {
            try
            {
                var factory = new MqttFactory();
                mqttClient = factory.CreateMqttClient();

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("broker.hivemq.com", 1883) // MQTT broker address and port
                    .WithCredentials("jose", "jose") // Set username and password
                    .WithClientId("MQTTLogger")
                    .WithCleanSession()
                    .Build();

                // Configurar el manejador de mensajes antes de conectar
                mqttClient.UseApplicationMessageReceivedHandler(async e =>
                {
                    try 
                    {
                        var topic = e.ApplicationMessage.Topic;
                        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        var timestamp = DateTime.Now;

                        using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                        {
                            await connection.OpenAsync();
                            var topicParts = topic.Split('/');
                            var userId = topicParts[0];
                            var sensorTopic = topicParts[1];

                            var command = new System.Data.SqlClient.SqlCommand(
                                "SELECT SensorID, SensorName, UserID, Topic FROM Sensors WHERE UserID = @UserID AND Topic = @Topic",
                                connection
                            );
                            command.Parameters.AddWithValue("@UserID", userId);
                            command.Parameters.AddWithValue("@Topic", sensorTopic);

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    var user = reader["UserID"].ToString();
                                    var top = reader["Topic"].ToString();
                                    var sensorId = reader["SensorID"].ToString();
                                    var sensorName = reader["SensorName"].ToString();

                                    // Crear nombre del archivo usando el topic completo
                                    var fileName = topic.Replace("/", "_") + ".log";
                                    var logPath = Path.Combine(logDirectory, fileName);

                                    // Asegurarnos de que el directorio existe
                                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));

                                    // Crear o escribir en el archivo
                                    try 
                                    {
                                        PrependToFile(
                                            logPath,
                                            $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] {sensorName} - Valor: {payload}\n"
                                        );

                                        // Log de depuración
                                        PrependToFile(
                                            Path.Combine(logDirectory, "service.log"),
                                            $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Log creado en: {logPath}\n"
                                        );
                                    }
                                    catch (Exception ex)
                                    {
                                        PrependToFile(
                                            Path.Combine(logDirectory, "error.log"),
                                            $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Error escribiendo en {logPath}: {ex.Message}\n"
                                        );
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PrependToFile(
                            Path.Combine(logDirectory, "error.log"),
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Error procesando mensaje: {ex.Message}\n{ex.StackTrace}\n"
                        );
                    }
                });

                await mqttClient.ConnectAsync(options);

                // Suscribirse a los topics usando conexión SQL directa
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var command = new System.Data.SqlClient.SqlCommand(
                        "SELECT UserID, Topic FROM Sensors",
                        connection
                    );

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var topic = $"{reader["UserID"]}/{reader["Topic"]}";
                            await mqttClient.SubscribeAsync(topic);
                            PrependToFile(
                                Path.Combine(logDirectory, "service.log"),
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Suscrito al topic: {topic}\n"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrependToFile(
                    Path.Combine(logDirectory, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Error: {ex.Message}\n{ex.StackTrace}\n"
                );
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
                    PrependToFile(
                        Path.Combine(logDirectory, "database.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Intentando acceder a la base de datos...\n"
                    );

                    await connection.OpenAsync();
                    
                    // Actualizada la consulta para incluir GreenHouse
                    var command = new System.Data.SqlClient.SqlCommand(
                        @"SELECT s.SensorID, s.SensorName, s.Topic, g.UserID, g.GreenHouseID 
                          FROM Sensors s 
                          INNER JOIN GreenHouses g ON s.GreenHouseID = g.GreenHouseID", 
                        connection
                    );

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var sensorsCount = 0;
                        while (await reader.ReadAsync())
                        {
                            sensorsCount++;
                            PrependToFile(
                                Path.Combine(logDirectory, "database.log"),
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Sensor:\n" +
                                $"  ID: {reader["SensorID"]}\n" +
                                $"  Nombre: {reader["SensorName"]}\n" +
                                $"  Topic: {reader["Topic"]}\n" +
                                $"  GreenHouseID: {reader["GreenHouseID"]}\n" +
                                $"  UserID: {reader["UserID"]}\n" +
                                "----------------------------------------\n"
                            );
                        }
                        
                        PrependToFile(
                            Path.Combine(logDirectory, "database.log"),
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Número total de sensores: {sensorsCount}\n"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                PrependToFile(
                    Path.Combine(logDirectory, "database.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {ex.Message}\n{ex.StackTrace}\n"
                );
            }
        }

        private async Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            
            try
            {
                using (var connection = new System.Data.SqlClient.SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    var topicParts = topic.Split('/');
                    var greenhouseId = int.Parse(topicParts[0]);
                    var sensorTopic = topicParts[1];

                    var command = new System.Data.SqlClient.SqlCommand(
                        @"SELECT s.SensorID, s.SensorName, g.UserID, s.Topic 
                          FROM Sensors s 
                          INNER JOIN GreenHouses g ON s.GreenHouseID = g.GreenHouseID 
                          WHERE s.GreenHouseID = @GreenHouseID AND s.Topic LIKE @Topic + '%'",
                        connection
                    );
                    command.Parameters.AddWithValue("@GreenHouseID", greenhouseId);
                    command.Parameters.AddWithValue("@Topic", sensorTopic);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var fileName = $"{greenhouseId}_{sensorTopic}.log";
                            var logPath = Path.Combine(logDirectory, fileName);

                            Directory.CreateDirectory(Path.GetDirectoryName(logPath));

                            PrependToFile(
                                logPath,
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {payload}\n"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrependToFile(
                    Path.Combine(logDirectory, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Error procesando mensaje: {ex.Message}\n"
                );
            }
        }
    }
}
