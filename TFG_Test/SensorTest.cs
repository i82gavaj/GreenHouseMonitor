using NUnit.Framework;
using TFGv1_1.Models;
using TFGv1_1.Controllers;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Entity;
using System.Security.Principal;
using System.Web;
using System.Security.Claims;
using System;
using System.Data.Entity.Infrastructure;

namespace TFG_Test
{
    public class SensorTests
    {
        private SensorController _controller;
        private Mock<ApplicationDbContext> _mockContext;
        private Mock<IUserStore<ApplicationUser>> _mockUserStore;
        private string _userId;
        private GreenHouse _testGreenhouse;

        [SetUp]
        public void Setup()
        {
            // Configurar el mock del contexto
            _mockContext = new Mock<ApplicationDbContext>();
            _mockUserStore = new Mock<IUserStore<ApplicationUser>>();
            _userId = "test-user-id";

            // Configurar el User.Identity
            var identity = new GenericIdentity(_userId);
            var principal = new GenericPrincipal(identity, null);

            // Mock del HttpServerUtility
            var mockServer = new Mock<HttpServerUtilityBase>();
            mockServer.Setup(s => s.MapPath(It.IsAny<string>()))
                .Returns((string path) => $"C:\\TestPath{path}");

            // Mock del HttpContextBase con Server
            var context = new Mock<HttpContextBase>();
            context.Setup(c => c.User).Returns(principal);
            context.Setup(c => c.Server).Returns(mockServer.Object);

            var controllerContext = new ControllerContext
            {
                HttpContext = context.Object
            };

            // Crear un invernadero de prueba
            _testGreenhouse = new GreenHouse
            {
                GreenHouseID = "test-greenhouse-1",
                UserID = _userId,
                Name = "Test Greenhouse",
                Location = "Test Location",
                Area = 100
            };

            // Configurar el mock del DbSet para GreenHouses
            var greenhouses = new List<GreenHouse> { _testGreenhouse }.AsQueryable();
            var mockGreenHouseSet = new Mock<DbSet<GreenHouse>>();
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Provider).Returns(greenhouses.Provider);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Expression).Returns(greenhouses.Expression);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.ElementType).Returns(greenhouses.ElementType);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.GetEnumerator()).Returns(() => greenhouses.GetEnumerator());

            // Configurar el DbSet para GreenHouses
            _mockContext.Setup(c => c.GreenHouses).Returns(mockGreenHouseSet.Object);

            // Configurar el mock del DbSet para Sensors con datos de prueba
            var sensors = new List<Sensor>
            {
                new Sensor
                {
                    SensorID = 1,
                    SensorName = "Test Sensor",
                    SensorType = SensorType.Temperature,
                    Units = Units.GCelsius,
                    Topic = "test/topic",
                    GreenHouseID = _testGreenhouse.GreenHouseID,
                    GreenHouse = _testGreenhouse,
                    LogFile = new SensorLogFile 
                    { 
                        FilePath = "test.log"
                    }
                }
            };
            var queryableSensors = sensors.AsQueryable();

            var mockSet = new Mock<DbSet<Sensor>>();

            // Setup básico de IQueryable
            mockSet.As<IQueryable<Sensor>>().Setup(m => m.Provider).Returns(queryableSensors.Provider);
            mockSet.As<IQueryable<Sensor>>().Setup(m => m.Expression).Returns(queryableSensors.Expression);
            mockSet.As<IQueryable<Sensor>>().Setup(m => m.ElementType).Returns(queryableSensors.ElementType);
            mockSet.As<IQueryable<Sensor>>().Setup(m => m.GetEnumerator()).Returns(() => queryableSensors.GetEnumerator());

            // Setup para Include que retorna el mismo mock
            _mockContext.Setup(c => c.Sensors).Returns(mockSet.Object);
            _mockContext.Setup(m => m.Sensors.Include(It.IsAny<string>())).Returns(mockSet.Object);

            // Setup para Add y Remove
            mockSet.Setup(m => m.Add(It.IsAny<Sensor>())).Returns<Sensor>(s => { sensors.Add(s); return s; });
            mockSet.Setup(m => m.Remove(It.IsAny<Sensor>())).Returns<Sensor>(s => { sensors.Remove(s); return s; });
            mockSet.Setup(m => m.Find(It.IsAny<object[]>()))
                .Returns<object[]>(ids => sensors.FirstOrDefault(s => s.SensorID == (int)ids[0]));

            // Setup para SaveChanges
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Configurar el User.Identity y ControllerContext
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, _userId));

            // Inicializar el controlador con el ControllerContext
            _controller = new SensorController(_mockContext.Object);
            _controller.ControllerContext = controllerContext;
        }

        [TearDown]
        public void TearDown()
        {
            _controller?.Dispose();
            _mockContext?.Object?.Dispose();
            _mockUserStore?.Object?.Dispose();
        }

        [Test]
        public void Create_ValidSensor_ReturnsRedirectToActionResult()
        {
            // Arrange
            var newSensor = new Sensor
            {
                SensorName = "New Test Sensor",
                SensorType = SensorType.Temperature,
                Units = Units.GCelsius,
                Topic = "new/test/topic",
                GreenHouseID = _testGreenhouse.GreenHouseID
            };

            // Act
            var result = _controller.Create(newSensor) as RedirectToRouteResult;

            // Assert
            Assert.That(result, Is.Not.Null, "RedirectToRouteResult no debería ser null");
            Assert.That(result.RouteValues["action"], Is.EqualTo("Index"));
            _mockContext.Verify(m => m.Sensors.Add(It.IsAny<Sensor>()), Times.Once());
            _mockContext.Verify(m => m.SaveChanges(), Times.Once());
        }

        [Test]
        public void Read_ExistingSensor_ReturnsSensor()
        {
            // Arrange
            int sensorId = 1;

            // Act
            var result = _controller.Details(sensorId) as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null, "ViewResult no debería ser null");
            var sensor = result.Model as Sensor;
            Assert.That(sensor, Is.Not.Null, "El modelo no debería ser null");
            Assert.That(sensor.SensorID, Is.EqualTo(sensorId));
        }

        [Test]
        public void Update_ValidSensor_ReturnsRedirectToActionResult()
        {
            // Arrange
            var existingSensor = new Sensor
            {
                SensorID = 1,
                SensorName = "Test Sensor",
                SensorType = SensorType.Temperature,
                Units = Units.GCelsius,
                Topic = "test/topic",
                GreenHouseID = _testGreenhouse.GreenHouseID,
                GreenHouse = _testGreenhouse
            };

            var updatedSensor = new Sensor
            {
                SensorID = 1,
                SensorName = "Updated Sensor",
                SensorType = SensorType.Temperature,
                Units = Units.GCelsius,
                Topic = "updated/topic",
                GreenHouseID = _testGreenhouse.GreenHouseID
            };

            // Setup para Find que devuelve el sensor existente
            _mockContext.Setup(m => m.Sensors.Find(It.IsAny<object[]>()))
                .Returns(existingSensor);

            // Act
            var result = _controller.Edit(updatedSensor) as RedirectToRouteResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.RouteValues["action"], Is.EqualTo("Index"));
            _mockContext.Verify(m => m.SaveChanges(), Times.Once());
        }

        [Test]
        public void Delete_ExistingSensor_ReturnsRedirectToActionResult()
        {
            // Arrange
            int sensorId = 1;

            // Crear el sensor de prueba con su archivo de log
            var testSensor = new Sensor
            {
                SensorID = sensorId,
                SensorName = "Test Sensor",
                GreenHouseID = _testGreenhouse.GreenHouseID,
                GreenHouse = _testGreenhouse,
                LogFile = new SensorLogFile 
                { 
                    FilePath = "test.log",
                    SensorId = sensorId
                }
            };

            // Configurar el mock del DbSet para Sensors
            var sensors = new List<Sensor> { testSensor }.AsQueryable();
            var mockSensorSet = new Mock<DbSet<Sensor>>();
            mockSensorSet.As<IQueryable<Sensor>>().Setup(m => m.Provider).Returns(sensors.Provider);
            mockSensorSet.As<IQueryable<Sensor>>().Setup(m => m.Expression).Returns(sensors.Expression);
            mockSensorSet.As<IQueryable<Sensor>>().Setup(m => m.ElementType).Returns(sensors.ElementType);
            mockSensorSet.As<IQueryable<Sensor>>().Setup(m => m.GetEnumerator()).Returns(() => sensors.GetEnumerator());
            mockSensorSet.Setup(m => m.Find(It.IsAny<object[]>())).Returns(testSensor);
            mockSensorSet.Setup(m => m.Include(It.IsAny<string>())).Returns(mockSensorSet.Object);

            // Configurar el mock del DbSet para SensorLogFiles
            var logFiles = new List<SensorLogFile> { testSensor.LogFile }.AsQueryable();
            var mockLogFileSet = new Mock<DbSet<SensorLogFile>>();
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.Provider).Returns(logFiles.Provider);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.Expression).Returns(logFiles.Expression);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.ElementType).Returns(logFiles.ElementType);
            mockLogFileSet.As<IQueryable<SensorLogFile>>().Setup(m => m.GetEnumerator()).Returns(() => logFiles.GetEnumerator());

            // Configurar el mock del contexto
            _mockContext.Setup(c => c.Sensors).Returns(mockSensorSet.Object);
            _mockContext.Setup(c => c.SensorLogFiles).Returns(mockLogFileSet.Object);
            _mockContext.Setup(m => m.SaveChanges()).Returns(1);

            // Configurar el mock del HttpContext y Server
            var mockHttpContext = new Mock<HttpContextBase>();
            var mockServer = new Mock<HttpServerUtilityBase>();
            mockServer.Setup(x => x.MapPath(It.IsAny<string>()))
                .Returns((string path) => $"C:\\TestPath{path}");
            
            var identity = new GenericIdentity(_userId);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, _userId));
            var principal = new GenericPrincipal(identity, null);
            
            mockHttpContext.Setup(x => x.Server).Returns(mockServer.Object);
            mockHttpContext.Setup(x => x.User).Returns(principal);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = mockHttpContext.Object
            };

            // Act
            var result = _controller.DeleteConfirmed(sensorId) as RedirectToRouteResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.RouteValues["action"], Is.EqualTo("Index"));
            _mockContext.Verify(m => m.Sensors.Remove(It.IsAny<Sensor>()), Times.Once());
            _mockContext.Verify(m => m.SensorLogFiles.Remove(It.IsAny<SensorLogFile>()), Times.Once());
            _mockContext.Verify(m => m.SaveChanges(), Times.Once());
        }

        [Test]
        public void Create_InvalidSensor_ReturnsViewResult()
        {
            // Arrange
            var invalidSensor = new Sensor(); // Sin propiedades requeridas
            _controller.ModelState.AddModelError("", "Test error");

            // Act
            var result = _controller.Create(invalidSensor) as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ViewName, Is.Empty.Or.EqualTo("Create"));
            Assert.That(result.Model, Is.EqualTo(invalidSensor));
        }

        [Test]
        public void Read_NonExistentSensor_ReturnsNotFound()
        {
            // Arrange
            int nonExistentId = 999;

            // Act
            var result = _controller.Details(nonExistentId) as HttpNotFoundResult;

            // Assert
            Assert.That(result, Is.Not.Null);
        }

        private void SetupGreenHouseMock(IQueryable<GreenHouse> greenhouses)
        {
            var mockGreenHouseSet = new Mock<DbSet<GreenHouse>>();
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Provider).Returns(greenhouses.Provider);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.Expression).Returns(greenhouses.Expression);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.ElementType).Returns(greenhouses.ElementType);
            mockGreenHouseSet.As<IQueryable<GreenHouse>>().Setup(m => m.GetEnumerator()).Returns(() => greenhouses.GetEnumerator());
            _mockContext.Setup(c => c.GreenHouses).Returns(mockGreenHouseSet.Object);
        }

        [Test, Category("EmptyGreenhouses")]
        public void Create_WithoutGreenhouses_ReturnsViewWithError()
        {
            // Act
            var result = _controller.Create() as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ViewData.ModelState.Count, Is.EqualTo(1));
            Assert.That(result.ViewData.ModelState.Values.First().Errors[0].ErrorMessage, 
                Is.EqualTo("Debe crear un invernadero antes de añadir sensores."));
        }

        [SetUp]
        public void SetupEmptyGreenhouses()
        {
            if (TestContext.CurrentContext.Test.Properties["Category"]?.Contains("EmptyGreenhouses") == true)
            {
                var emptyGreenhouses = new List<GreenHouse>().AsQueryable();
                SetupGreenHouseMock(emptyGreenhouses);
            }
        }

        [Test]
        public void Simulation_ValidSensor_ReturnsViewResult()
        {
            // Arrange
            int sensorId = 1;

            // Act
            var result = _controller.Simulation(sensorId) as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            var sensor = result.Model as Sensor;
            Assert.That(sensor, Is.Not.Null);
            Assert.That(sensor.SensorID, Is.EqualTo(sensorId));
        }

        [Test]
        public void Simulation_NullId_ReturnsBadRequest()
        {
            // Act
            var result = _controller.Simulation(null) as HttpStatusCodeResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public void Delete_UnauthorizedUser_ReturnsUnauthorized()
        {
            // Arrange
            int sensorId = 1;
            var unauthorizedUserId = "unauthorized-user-id";
            var identity = new GenericIdentity(unauthorizedUserId);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, unauthorizedUserId));
            var principal = new GenericPrincipal(identity, null);

            var mockHttpContext = new Mock<HttpContextBase>();
            mockHttpContext.Setup(x => x.User).Returns(principal);
            mockHttpContext.Setup(x => x.Server).Returns(Mock.Of<HttpServerUtilityBase>());

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = mockHttpContext.Object
            };

            // Act
            var result = _controller.DeleteConfirmed(sensorId) as HttpStatusCodeResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(401));
        }

        [Test]
        public void Graphs_ReturnsUserSensors()
        {
            // Act
            var result = _controller.Graphs() as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            var sensors = result.Model as List<Sensor>;
            Assert.That(sensors, Is.Not.Null);
            Assert.That(sensors.Count, Is.EqualTo(1));
            Assert.That(sensors[0].GreenHouse.UserID, Is.EqualTo(_userId));
        }

        [Test]
        public void Edit_InvalidModelState_ReturnsViewWithModel()
        {
            // Arrange
            var sensor = new Sensor
            {
                SensorID = 1,
                SensorName = "Test Sensor"
                // Faltan campos requeridos
            };
            _controller.ModelState.AddModelError("", "Test error");

            // Act
            var result = _controller.Edit(sensor) as ViewResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Model, Is.EqualTo(sensor));
        }

        [Test]
        public void Details_NullId_ReturnsBadRequest()
        {
            // Act
            var result = _controller.Details(null) as HttpStatusCodeResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(400));
        }
    }
}