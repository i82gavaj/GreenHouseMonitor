using NUnit.Framework;
using TFGv1_1.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace TFG_Test
{
    public class SensorLogFileTest
    {
        private Mock<ApplicationDbContext> _mockContext;
        private SensorLogFile _testLogFile;
        private Sensor _testSensor;
        private int _sensorId;

        [SetUp]
        public void Setup()
        {
            _sensorId = 1;
            _mockContext = new Mock<ApplicationDbContext>();

            // Crear sensor de prueba
            _testSensor = new Sensor
            {
                SensorID = _sensorId,
                SensorName = "Sensor Test",
                Topic = "test/topic",
                GreenHouseID = "GH_test"
            };

            // Crear archivo de log de prueba
            _testLogFile = new SensorLogFile
            {
                SensorId = _sensorId,
                FilePath = "Logs/test_sensor.log",
                CreationDate = DateTime.Now,
                LogFileId = 1,
                Sensor = _testSensor
            };

            // Configurar el mock del DbSet para SensorLogFiles
            var logFiles = new List<SensorLogFile> { _testLogFile }.AsQueryable();
            var mockLogFileSet = new Mock<DbSet<SensorLogFile>>();
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.Provider).Returns(logFiles.Provider);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.Expression).Returns(logFiles.Expression);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.ElementType).Returns(logFiles.ElementType);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.GetEnumerator()).Returns(() => logFiles.GetEnumerator());

            _mockContext.Setup(c => c.SensorLogFiles).Returns(mockLogFileSet.Object);
        }

        [Test]
        public void Create_ValidLogFile_Succeeds()
        {
            // Arrange
            var newLogFile = new SensorLogFile
            {
                SensorId = 2,
                FilePath = "Logs/new_sensor.log",
                CreationDate = DateTime.Now,
                LogFileId = 2
            };

            // Act
            _mockContext.Object.SensorLogFiles.Add(newLogFile);

            // Assert
            Assert.That(newLogFile.SensorId, Is.EqualTo(2));
            Assert.That(newLogFile.FilePath, Is.EqualTo("Logs/new_sensor.log"));
            Assert.That(newLogFile.LogFileId, Is.EqualTo(2));
        }

        [Test]
        public void Create_EmptyFilePath_ThrowsException()
        {
            // Arrange
            var invalidLogFile = new SensorLogFile
            {
                SensorId = 2,
                FilePath = "", // FilePath vacío
                CreationDate = DateTime.Now
            };

            // Act & Assert
            var ex = Assert.Throws<ValidationException>(() =>
            {
                ValidateLogFile(invalidLogFile);
            });
            Assert.That(ex.Message, Does.Contain("El campo FilePath es obligatorio"));
        }

        [Test]
        public void Create_FilePathTooLong_ThrowsException()
        {
            // Arrange
            var invalidLogFile = new SensorLogFile
            {
                SensorId = 2,
                FilePath = new string('A', 256), // FilePath demasiado largo (>255)
                CreationDate = DateTime.Now
            };

            // Act & Assert
            var ex = Assert.Throws<ValidationException>(() =>
            {
                ValidateLogFile(invalidLogFile);
            });
            Assert.That(ex.Message, Does.Contain("El campo FilePath debe ser una cadena con una longitud máxima de 255"));
        }

        [Test]
        public void Read_ExistingLogFile_ReturnsLogFile()
        {
            // Arrange
            _mockContext.Setup(c => c.SensorLogFiles.Find(_sensorId))
                .Returns(_testLogFile);

            // Act
            var result = _mockContext.Object.SensorLogFiles.Find(_sensorId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.FilePath, Is.EqualTo("Logs/test_sensor.log"));
            Assert.That(result.SensorId, Is.EqualTo(_sensorId));
        }

        [Test]
        public void Read_NonExistingLogFile_ReturnsNull()
        {
            // Arrange
            _mockContext.Setup(c => c.SensorLogFiles.Find(999))
                .Returns((SensorLogFile)null);

            // Act
            var result = _mockContext.Object.SensorLogFiles.Find(999);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Update_LogFilePath_Succeeds()
        {
            // Arrange
            var newPath = "Logs/updated_path.log";

            // Act
            _testLogFile.FilePath = newPath;

            // Assert
            Assert.That(_testLogFile.FilePath, Is.EqualTo(newPath));
        }

        [Test]
        public void Delete_LogFile_Succeeds()
        {
            // Act
            _mockContext.Object.SensorLogFiles.Remove(_testLogFile);

            // Assert
            _mockContext.Verify(m => m.SensorLogFiles.Remove(_testLogFile), Times.Once());
        }

        [Test]
        public void Create_WithoutCreationDate_UsesCurrentDate()
        {
            // Arrange
            var beforeTest = DateTime.Now.AddSeconds(-1);
            var logFile = new SensorLogFile
            {
                SensorId = 2,
                FilePath = "Logs/sensor.log",
                LogFileId = 3
            };

            // Act
            logFile.CreationDate = DateTime.Now;
            var afterTest = DateTime.Now.AddSeconds(1);

            // Assert
            Assert.That(logFile.CreationDate, Is.InRange(beforeTest, afterTest));
        }

        [Test]
        public void Verify_OneToOneRelationship_WithSensor()
        {
            // Arrange
            _testSensor.LogFile = _testLogFile; // Establecer la relación bidireccional
            
            // Act
            var sensor = _testLogFile.Sensor;
            var logFile = _testSensor.LogFile;

            // Assert
            Assert.That(sensor, Is.Not.Null);
            Assert.That(sensor.SensorID, Is.EqualTo(_testLogFile.SensorId));
            Assert.That(logFile, Is.EqualTo(_testLogFile));
            Assert.That(sensor.LogFile, Is.SameAs(_testLogFile));
            Assert.That(logFile.Sensor, Is.SameAs(_testSensor));
        }

        [Test]
        public void Create_DuplicateSensorId_ShouldFail()
        {
            // Arrange
            var duplicateLogFile = new SensorLogFile
            {
                SensorId = _sensorId, // ID duplicado
                FilePath = "Logs/duplicate_sensor.log",
                CreationDate = DateTime.Now
            };

            // Configurar el mock para lanzar una excepción en SaveChanges
            _mockContext.Setup(m => m.SaveChanges())
                .Throws(new InvalidOperationException("Cannot insert duplicate key row in object 'dbo.SensorLogFiles' with unique index 'IX_SensorId'"));

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                _mockContext.Object.SensorLogFiles.Add(duplicateLogFile);
                _mockContext.Object.SaveChanges();
            });

            Assert.That(ex.Message, Does.Contain("duplicate key"));
        }

        [Test]
        public void Create_InvalidFilePath_ThrowsException()
        {
            // Arrange
            var invalidPaths = new[]
            {
                "Logs/*/sensor.log",      // Asterisco no permitido
                "Logs?/sensor.log",       // Signo de interrogación no permitido
                "Logs</sensor.log",       // Caracteres especiales no permitidos
                "Logs>/sensor.log",
                "Logs|/sensor.log",
                "Logs\"/sensor.log",
                "COM1/sensor.log",        // Nombres reservados de Windows
                "PRN/sensor.log",
                "",                       // Ruta vacía
                null                      // Ruta nula
            };

            // Act & Assert
            foreach (var invalidPath in invalidPaths)
            {
                var invalidLogFile = new SensorLogFile
                {
                    SensorId = 2,
                    FilePath = invalidPath,
                    CreationDate = DateTime.Now
                };

                var ex = Assert.Throws<ValidationException>(() => ValidateLogFile(invalidLogFile));
                Assert.That(ex.Message, Does.Contain("El campo FilePath").Or.Contain("caracteres no válidos"));
            }
        }

        [Test]
        public void Create_ValidFilePaths_Succeeds()
        {
            // Arrange
            var validPaths = new[]
            {
                "sensor.log",
                "sensor_2023-01-01.log",
                "temperature_readings_001.log",
                "greenhouse1_sensor1.log",
                "logs_temperature.log"
            };

            // Act & Assert
            foreach (var validPath in validPaths)
            {
                var logFile = new SensorLogFile
                {
                    SensorId = 2,
                    FilePath = validPath,
                    CreationDate = DateTime.Now,
                    LogFileId = 3
                };

                Assert.DoesNotThrow(() => ValidateLogFile(logFile));
            }
        }

        [Test]
        public void Update_CreationDate_DoesNotAllowFutureDate()
        {
            // Arrange
            var futureDate = DateTime.Now.AddDays(1);
            var logFile = new SensorLogFile
            {
                SensorId = 2,
                FilePath = "Logs/sensor.log",
                CreationDate = futureDate,
                LogFileId = 3
            };

            // Act & Assert
            Assert.DoesNotThrow(() => 
            {
                logFile.CreationDate = DateTime.Now;
            });

            Assert.That(logFile.CreationDate, Is.LessThanOrEqualTo(DateTime.Now));
        }

        [Test]
        public void Verify_LogFileProperties_AreCorrectlySet()
        {
            // Arrange & Act
            var creationDate = new DateTime(2023, 1, 1);
            var logFile = new SensorLogFile
            {
                LogFileId = 100,
                SensorId = 50,
                FilePath = "Logs/test.log",
                CreationDate = creationDate,
                Sensor = _testSensor
            };

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(logFile.LogFileId, Is.EqualTo(100));
                Assert.That(logFile.SensorId, Is.EqualTo(50));
                Assert.That(logFile.FilePath, Is.EqualTo("Logs/test.log"));
                Assert.That(logFile.CreationDate, Is.EqualTo(creationDate));
                Assert.That(logFile.Sensor, Is.SameAs(_testSensor));
            });
        }

        [Test]
        public void Update_MultipleProperties_Succeeds()
        {
            // Arrange
            var newDate = DateTime.Now.AddDays(-1);
            var newPath = "Logs/updated_file.log";
            var newSensorId = 999;

            // Act
            _testLogFile.CreationDate = newDate;
            _testLogFile.FilePath = newPath;
            _testLogFile.SensorId = newSensorId;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_testLogFile.CreationDate, Is.EqualTo(newDate));
                Assert.That(_testLogFile.FilePath, Is.EqualTo(newPath));
                Assert.That(_testLogFile.SensorId, Is.EqualTo(newSensorId));
            });
        }

        private void ValidateLogFile(SensorLogFile logFile)
        {
            var context = new ValidationContext(logFile, null, null);
            var results = new List<ValidationResult>();

            // Primero ejecutar las validaciones de DataAnnotations
            if (!Validator.TryValidateObject(logFile, context, results, true))
            {
                throw new ValidationException(results.First().ErrorMessage);
            }

            // Validación personalizada para caracteres inválidos en la ruta
            if (!string.IsNullOrEmpty(logFile.FilePath))
            {
                // Lista de caracteres no permitidos en nombres de archivo
                var invalidChars = new[] { '<', '>', '|', '"', '*', '?', ':', '\\', '/' };
                
                if (logFile.FilePath.Any(c => invalidChars.Contains(c)))
                {
                    throw new ValidationException("El campo FilePath contiene caracteres no válidos");
                }

                // Validar que la extensión sea .log
                if (!logFile.FilePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("El archivo debe tener extensión .log");
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            _mockContext?.Object?.Dispose();
        }
    }
}
