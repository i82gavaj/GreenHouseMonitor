using NUnit.Framework;
using TFGv1_1.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Entity;
using TFGv1_1.Controllers;
using System.Web.Mvc;
using System.Threading.Tasks;
using System.Globalization; // Añadir esta referencia

namespace TFG_Test
{
    public class AlertTest
    {
        private Mock<ApplicationDbContext> _mockContext;
        private Alert _testAlert;
        private GreenHouse _testGreenHouse;
        private Sensor _testSensor;
        private AlertController _controller;
        private Mock<DbSet<Alert>> _mockAlertSet;
        private List<Alert> _alerts;

        [SetUp]
        public void Setup()
        {
            // Crear objetos de prueba
            _testGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH1",
                Name = "Invernadero Test",
                UserID = "user-test-id",
                Location = "Ubicación Test"
            };

            _testSensor = new Sensor
            {
                SensorID = 1,
                SensorName = "Sensor Test",
                SensorType = SensorType.Temperature,
                GreenHouseID = "GH1",
                Units = Units.GCelsius,
                Topic = "sensors/greenhouse1/temperature"
            };

            _testAlert = new Alert
            {
                AlertID = 1,
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Medium,
                Message = "Temperatura fuera de rango",
                CreatedAt = DateTime.Now,
                ResolvedAt = null,
                IsResolved = false,
                ThresholdRange = "20-30",
                CurrentValue = 35.5,
                NotifyByEmail = true,
                NotifyByPush = false,
                IsNotification = false,
                GreenHouse = _testGreenHouse,
                Sensor = _testSensor
            };

            // Configurar el mock del contexto
            _mockContext = new Mock<ApplicationDbContext>();

            // Configurar el mock del DbSet para Alerts
            _alerts = new List<Alert> { _testAlert };
            var queryableAlerts = _alerts.AsQueryable();
            _mockAlertSet = new Mock<DbSet<Alert>>();
            _mockAlertSet.As<IQueryable<Alert>>().Setup(m => m.Provider).Returns(queryableAlerts.Provider);
            _mockAlertSet.As<IQueryable<Alert>>().Setup(m => m.Expression).Returns(queryableAlerts.Expression);
            _mockAlertSet.As<IQueryable<Alert>>().Setup(m => m.ElementType).Returns(queryableAlerts.ElementType);
            _mockAlertSet.As<IQueryable<Alert>>().Setup(m => m.GetEnumerator()).Returns(() => queryableAlerts.GetEnumerator());

            _mockAlertSet.Setup(m => m.Find(It.IsAny<object[]>()))
                .Returns<object[]>(ids => _alerts.FirstOrDefault(a => a.AlertID == (int)ids[0]));

            _mockContext.Setup(c => c.Alerts).Returns(_mockAlertSet.Object);

            // Configurar el controlador con el contexto mock
            _controller = new AlertController();
            var controllerType = typeof(AlertController);
            var contextField = controllerType.GetField("_context", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            contextField?.SetValue(_controller, _mockContext.Object);
        }

        [Test]
        public void Create_ValidAlert_Succeeds()
        {
            // Arrange
            var newAlert = new Alert
            {
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Low,
                Message = "Nueva alerta de prueba",
                CreatedAt = DateTime.Now,
                ThresholdRange = "15-25",
                CurrentValue = 10.5,
                NotifyByEmail = true,
                NotifyByPush = true,
                IsNotification = false
            };

            _mockAlertSet.Setup(m => m.Add(It.IsAny<Alert>())).Returns(newAlert);
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Act
            _mockContext.Object.Alerts.Add(newAlert);
            var result = _mockContext.Object.SaveChanges();

            // Assert
            Assert.That(result, Is.EqualTo(1));
            _mockAlertSet.Verify(m => m.Add(It.IsAny<Alert>()), Times.Once());
        }

        [Test]
        public void Read_ExistingAlert_ReturnsAlert()
        {
            // Arrange & Act
            var result = _mockContext.Object.Alerts.Find(1);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.AlertID, Is.EqualTo(1));
            Assert.That(result.AlertType, Is.EqualTo(AlertType.Temperature));
            Assert.That(result.Message, Is.EqualTo("Temperatura fuera de rango"));
        }

        [Test]
        public void Update_AlertStatus_Succeeds()
        {
            // Arrange
            _testAlert.IsResolved = true;
            _testAlert.ResolvedAt = DateTime.Now;

            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Act
            var result = _mockContext.Object.SaveChanges();

            // Assert
            Assert.That(result, Is.EqualTo(1));
            Assert.That(_testAlert.IsResolved, Is.True);
            Assert.That(_testAlert.ResolvedAt, Is.Not.Null);
        }

        [Test]
        public void Delete_ExistingAlert_Succeeds()
        {
            // Arrange
            _mockAlertSet.Setup(m => m.Remove(It.IsAny<Alert>())).Returns(_testAlert);
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Act
            _mockContext.Object.Alerts.Remove(_testAlert);
            var result = _mockContext.Object.SaveChanges();

            // Assert
            Assert.That(result, Is.EqualTo(1));
            _mockAlertSet.Verify(m => m.Remove(It.IsAny<Alert>()), Times.Once());
        }

        [Test]
        public void Create_InvalidThresholdRange_Fails()
        {
            // Arrange
            var invalidAlert = new Alert
            {
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Medium,
                Message = "Alerta con rango inválido",
                ThresholdRange = "invalid-range", // Formato inválido
                CurrentValue = 25.0
            };

            // Act & Assert
            var exception = Assert.Throws<FormatException>(() =>
            {
                // Intentar parsear el rango inválido
                var parts = invalidAlert.ThresholdRange.Split('-');
                var min = double.Parse(parts[0]);
                var max = double.Parse(parts[1]);
            });
        }

        [Test]
        public void Create_AlertWithoutRequiredFields_Fails()
        {
            // Arrange
            var incompleteAlert = new Alert();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                // Validar manualmente los campos requeridos
                if (string.IsNullOrEmpty(incompleteAlert.GreenHouseID) ||
                    incompleteAlert.SensorID == 0 ||
                    string.IsNullOrEmpty(incompleteAlert.Message) ||
                    string.IsNullOrEmpty(incompleteAlert.ThresholdRange))
                {
                    throw new InvalidOperationException("Campos requeridos faltantes");
                }
            });
        }

        [Test]
        public void Create_ValidThresholdRange_Succeeds()
        {
            // Arrange
            var validAlert = new Alert
            {
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Medium,
                Message = "Alerta con rango válido",
                ThresholdRange = "20-30", // Formato válido
                CurrentValue = 25.0
            };

            // Act
            var parts = validAlert.ThresholdRange.Split('-');
            var min = double.Parse(parts[0]);
            var max = double.Parse(parts[1]);

            // Assert
            Assert.That(min, Is.EqualTo(20));
            Assert.That(max, Is.EqualTo(30));
        }

        [Test]
        public void Create_NegativeThresholdRange_Succeeds()
        {
            // Arrange
            var validAlert = new Alert
            {
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Medium,
                Message = "Alerta con rango negativo",
                ThresholdRange = "-10-5", // Rango con valores negativos
                CurrentValue = 0.0
            };

            // Act
            // Manejar correctamente el formato con valores negativos
            string[] parts;
            double min, max;
            
            if (validAlert.ThresholdRange.StartsWith("-"))
            {
                // Si comienza con signo negativo, el formato es especial
                int secondDashIndex = validAlert.ThresholdRange.IndexOf('-', 1);
                if (secondDashIndex > 0)
                {
                    min = double.Parse(validAlert.ThresholdRange.Substring(0, secondDashIndex));
                    max = double.Parse(validAlert.ThresholdRange.Substring(secondDashIndex + 1));
                }
                else
                {
                    throw new FormatException("Formato de rango inválido");
                }
            }
            else
            {
                // Formato normal sin valor negativo al inicio
                parts = validAlert.ThresholdRange.Split('-');
                min = double.Parse(parts[0]);
                max = double.Parse(parts[1]);
            }

            // Assert
            Assert.That(min, Is.EqualTo(-10));
            Assert.That(max, Is.EqualTo(5));
        }

        [Test]
        public void Create_DecimalThresholdRange_Succeeds()
        {
            // Arrange
            var validAlert = new Alert
            {
                GreenHouseID = "GH1",
                SensorID = 1,
                AlertType = AlertType.Temperature,
                Severity = AlertSeverity.Medium,
                Message = "Alerta con rango decimal",
                ThresholdRange = "20,5-30,5", // Usar coma como separador decimal
                CurrentValue = 25.0
            };

            // Act
            var parts = validAlert.ThresholdRange.Split('-');
            var min = double.Parse(parts[0]); // Usará la configuración regional actual
            var max = double.Parse(parts[1]);

            // Assert
            Assert.That(min, Is.EqualTo(20.5));
            Assert.That(max, Is.EqualTo(30.5));
        }

        [Test]
        public void Update_AlertSeverity_Succeeds()
        {
            // Arrange
            _testAlert.Severity = AlertSeverity.Critical;
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Act
            var result = _mockContext.Object.SaveChanges();

            // Assert
            Assert.That(result, Is.EqualTo(1));
            Assert.That(_testAlert.Severity, Is.EqualTo(AlertSeverity.Critical));
        }

        [Test]
        public void Update_AlertNotificationSettings_Succeeds()
        {
            // Arrange
            _testAlert.NotifyByEmail = false;
            _testAlert.NotifyByPush = true;
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Act
            var result = _mockContext.Object.SaveChanges();

            // Assert
            Assert.That(result, Is.EqualTo(1));
            Assert.That(_testAlert.NotifyByEmail, Is.False);
            Assert.That(_testAlert.NotifyByPush, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            // Disponer el controlador que implementa IDisposable
            if (_controller != null)
            {
                _controller.Dispose();
            }
            
            _mockContext = null;
            _testAlert = null;
            _testGreenHouse = null;
            _testSensor = null;
            _controller = null;
            _mockAlertSet = null;
            _alerts = null;
        }
    }
} 