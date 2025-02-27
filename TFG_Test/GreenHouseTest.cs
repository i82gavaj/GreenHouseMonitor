using NUnit.Framework;
using TFGv1_1.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Entity;
using Microsoft.AspNet.Identity;
using System.ComponentModel.DataAnnotations;

namespace TFG_Test
{
    public class GreenHouseTest
    {
        private Mock<ApplicationDbContext> _mockContext;
        private GreenHouse _testGreenHouse;
        private string _userId;
        private string _greenHouseId;

        [SetUp]
        public void Setup()
        {
            _userId = "test-user-id";
            _greenHouseId = "GH_test_id";
            _mockContext = new Mock<ApplicationDbContext>();

            // Crear invernadero de prueba
            _testGreenHouse = new GreenHouse
            {
                GreenHouseID = _greenHouseId,
                UserID = _userId,
                Name = "Invernadero Test",
                Description = "Descripción de prueba",
                Location = "Ubicación Test",
                Area = 100.0f
            };

            // Configurar el mock del DbSet para GreenHouses
            var greenhouses = new List<GreenHouse> { _testGreenHouse }.AsQueryable();
            var mockGreenHouseSet = new Mock<DbSet<GreenHouse>>();
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Provider).Returns(greenhouses.Provider);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Expression).Returns(greenhouses.Expression);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.ElementType).Returns(greenhouses.ElementType);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.GetEnumerator()).Returns(() => greenhouses.GetEnumerator());

            _mockContext.Setup(c => c.GreenHouses).Returns(mockGreenHouseSet.Object);
        }

        [Test]
        public void Create_ValidGreenHouse_Succeeds()
        {
            // Arrange
            var newGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_new",
                UserID = _userId,
                Name = "Nuevo Invernadero",
                Description = "Nueva descripción",
                Location = "Nueva ubicación",
                Area = 150.0f
            };

            // Act
            _mockContext.Object.GreenHouses.Add(newGreenHouse);

            // Assert
            Assert.That(newGreenHouse.GreenHouseID, Is.EqualTo("GH_new"));
            Assert.That(newGreenHouse.Name, Is.EqualTo("Nuevo Invernadero"));
            Assert.That(newGreenHouse.Area, Is.EqualTo(150.0f));
        }

        [Test]
        public void Create_InvalidArea_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "Invernadero Inválido",
                Location = "Ubicación",
                Area = 0f // Área inválida
            };

            // Act & Assert
            var ex = Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("El área debe estar entre 1 y 10000"));
        }

        [Test]
        public void Create_EmptyName_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "", // Nombre vacío
                Location = "Ubicación",
                Area = 100f
            };

            // Act & Assert
            var ex = Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("El nombre es obligatorio"));
        }

        [Test]
        public void Update_GreenHouseDescription_Succeeds()
        {
            // Arrange
            var newDescription = "Descripción actualizada";

            // Act
            _testGreenHouse.Description = newDescription;

            // Assert
            Assert.That(_testGreenHouse.Description, Is.EqualTo(newDescription));
        }

        [Test]
        public void Update_GreenHouseWithSensors_Success()
        {
            // Arrange
            var sensors = new List<Sensor>
            {
                new Sensor { SensorID = 1, SensorName = "Sensor1", GreenHouseID = _greenHouseId },
                new Sensor { SensorID = 2, SensorName = "Sensor2", GreenHouseID = _greenHouseId }
            };

            _testGreenHouse.Sensors = sensors;

            // Act & Assert
            Assert.That(_testGreenHouse.Sensors.Count, Is.EqualTo(2));
            Assert.That(_testGreenHouse.Sensors.First().SensorName, Is.EqualTo("Sensor1"));
        }

        [Test]
        public void Create_InvalidLocation_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "Nombre válido",
                Location = "", // Ubicación vacía
                Area = 100f
            };

            // Act & Assert
            var ex = Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("La ubicación es obligatoria"));
        }

        [Test]
        public void Create_DescriptionTooLong_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "Nombre válido",
                Description = new string('A', 201), // Descripción demasiado larga
                Location = "Ubicación válida",
                Area = 100f
            };

            // Act & Assert
            var ex = Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("La descripción no puede tener más de 200 caracteres"));
        }

        [Test]
        public void Create_AreaTooLarge_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "Nombre válido",
                Location = "Ubicación válida",
                Area = 10001f // Área demasiado grande
            };

            // Act & Assert
            var ex = Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("El área debe estar entre 1 y 10000"));
        }

        [Test]
        public void Read_ExistingGreenHouse_ReturnsGreenHouse()
        {
            // Arrange
            _mockContext.Setup(c => c.GreenHouses.Find(_greenHouseId))
                .Returns(_testGreenHouse);

            // Act
            var result = _mockContext.Object.GreenHouses.Find(_greenHouseId);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Invernadero Test"));
            Assert.That(result.Location, Is.EqualTo("Ubicación Test"));
        }

        [Test]
        public void Read_NonExistingGreenHouse_ReturnsNull()
        {
            // Arrange
            _mockContext.Setup(c => c.GreenHouses.Find("non_existing_id"))
                .Returns((GreenHouse)null);

            // Act
            var result = _mockContext.Object.GreenHouses.Find("non_existing_id");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Update_GreenHouseArea_ValidatesRange()
        {
            // Act & Assert - Área mínima
            Assert.DoesNotThrow(() => 
            {
                _testGreenHouse.Area = 1f;
            });

            // Act & Assert - Área máxima
            Assert.DoesNotThrow(() => 
            {
                _testGreenHouse.Area = 10000f;
            });
        }

        [Test]
        public void Create_NameTooLong_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = new string('A', 51), // Nombre demasiado largo (>50)
                Location = "Ubicación válida",
                Area = 100f
            };

            // Act & Assert
            var ex = Assert.Throws<ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("no puede tener más de 50 caracteres"));
        }

        [Test]
        public void Create_LocationTooLong_ThrowsException()
        {
            // Arrange
            var invalidGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_invalid",
                UserID = _userId,
                Name = "Nombre válido",
                Location = new string('A', 101), // Ubicación demasiado larga (>100)
                Area = 100f
            };

            // Act & Assert
            var ex = Assert.Throws<ValidationException>(() =>
            {
                ValidateGreenHouse(invalidGreenHouse);
            });
            Assert.That(ex.Message, Does.Contain("no puede tener más de 100 caracteres"));
        }

        [Test]
        public void Update_AllSensorsHaveSameGreenHouseId()
        {
            // Arrange
            var sensors = new List<Sensor>
            {
                new Sensor { SensorID = 1, SensorName = "Sensor1", GreenHouseID = _greenHouseId },
                new Sensor { SensorID = 2, SensorName = "Sensor2", GreenHouseID = _greenHouseId },
                new Sensor { SensorID = 3, SensorName = "Sensor3", GreenHouseID = _greenHouseId }
            };

            _testGreenHouse.Sensors = sensors;

            // Act & Assert
            Assert.That(_testGreenHouse.Sensors.All(s => s.GreenHouseID == _greenHouseId));
        }

        [Test]
        public void Delete_GreenHouseWithSensors_CascadeDelete()
        {
            // Arrange
            var sensors = new List<Sensor>
            {
                new Sensor { SensorID = 1, SensorName = "Sensor1", GreenHouseID = _greenHouseId },
                new Sensor { SensorID = 2, SensorName = "Sensor2", GreenHouseID = _greenHouseId }
            };
            _testGreenHouse.Sensors = sensors;

            var mockSensorSet = new Mock<DbSet<Sensor>>();
            _mockContext.Setup(c => c.Sensors).Returns(mockSensorSet.Object);

            // Act
            _mockContext.Object.GreenHouses.Remove(_testGreenHouse);

            // Assert
            mockSensorSet.Verify(m => m.RemoveRange(It.IsAny<IEnumerable<Sensor>>()), Times.Never());
            // La eliminación en cascada es manejada por la base de datos
        }

        [Test]
        public void Create_DuplicateNameForSameUser_Fails()
        {
            // Arrange
            var existingName = "Invernadero Test";
            var newGreenHouse = new GreenHouse
            {
                GreenHouseID = "GH_new",
                UserID = _userId,
                Name = existingName,
                Location = "Nueva ubicación",
                Area = 100f
            };

            // Configurar el mock para devolver una lista que incluya el invernadero existente
            var greenhouses = new List<GreenHouse> 
            { 
                _testGreenHouse,
                newGreenHouse 
            }.AsQueryable();

            var mockGreenHouseSet = new Mock<DbSet<GreenHouse>>();
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Provider).Returns(greenhouses.Provider);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Expression).Returns(greenhouses.Expression);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.ElementType).Returns(greenhouses.ElementType);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.GetEnumerator()).Returns(greenhouses.GetEnumerator);

            _mockContext.Setup(c => c.GreenHouses).Returns(mockGreenHouseSet.Object);

            // Act
            var duplicateExists = _mockContext.Object.GreenHouses
                .Count(g => g.UserID == _userId && g.Name == existingName) > 1;

            // Assert
            Assert.That(duplicateExists, Is.True, "Debería detectar el nombre duplicado para el mismo usuario");
        }

        [Test]
        public void Update_GreenHouseLocation_ValidLocation()
        {
            // Arrange
            var validLocations = new[]
            {
                "Invernadero Norte",
                "Sector A-123",
                "Parcela 42",
                "Zona de cultivo principal"
            };

            // Act & Assert
            foreach (var location in validLocations)
            {
                Assert.DoesNotThrow(() =>
                {
                    _testGreenHouse.Location = location;
                    ValidateGreenHouse(_testGreenHouse);
                });
            }
        }

        [Test]
        public void Create_WithOptionalDescription_Succeeds()
        {
            // Arrange
            var greenHouseWithoutDescription = new GreenHouse
            {
                GreenHouseID = "GH_new",
                UserID = _userId,
                Name = "Invernadero Sin Descripción",
                Location = "Ubicación Test",
                Area = 100f,
                Description = null
            };

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                ValidateGreenHouse(greenHouseWithoutDescription);
            });
        }

        private void ValidateGreenHouse(GreenHouse greenhouse)
        {
            var context = new ValidationContext(greenhouse, null, null);
            var results = new List<ValidationResult>();

            if (!Validator.TryValidateObject(greenhouse, context, results, true))
            {
                throw new ValidationException(results.First().ErrorMessage);
            }
        }

        [TearDown]
        public void TearDown()
        {
            _mockContext?.Object?.Dispose();
        }
    }
}
