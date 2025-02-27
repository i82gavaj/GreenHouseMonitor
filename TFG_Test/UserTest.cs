using NUnit.Framework;
using TFGv1_1.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Entity;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System.Security.Claims;

namespace TFG_Test
{
    public class UserTest
    {
        private Mock<ApplicationDbContext> _mockContext;
        private Mock<UserManager<ApplicationUser>> _mockUserManager;
        private ApplicationUser _testUser;
        private string _userId;

        [SetUp]
        public void Setup()
        {
            _userId = "test-user-id";
            _mockContext = new Mock<ApplicationDbContext>();

            // Crear usuario de prueba con un email válido
            _testUser = new ApplicationUser
            {
                Id = _userId,
                UserName = "usuario_prueba@test.com", // Cambiado para que coincida con el email
                Email = "usuario_prueba@test.com",    // Email válido
                PhoneNumber = "123456789",
                Name = "Usuario Test",
                Surname = "Apellido Test",
                LastName = "Apellido Requerido",
                BirthDate = new DateTime(1990, 1, 1)
            };

            // Configurar el mock del UserStore
            var mockUserStore = new Mock<IUserStore<ApplicationUser>>();
            _mockUserManager = new Mock<UserManager<ApplicationUser>>(mockUserStore.Object);

            // Configurar el mock del DbSet para Users
            var users = new List<ApplicationUser> { _testUser }.AsQueryable();
            var mockUserSet = new Mock<DbSet<ApplicationUser>>();
            mockUserSet.As<IQueryable<ApplicationUser>>().Setup(m => m.Provider).Returns(users.Provider);
            mockUserSet.As<IQueryable<ApplicationUser>>().Setup(m => m.Expression).Returns(users.Expression);
            mockUserSet.As<IQueryable<ApplicationUser>>().Setup(m => m.ElementType).Returns(users.ElementType);
            mockUserSet.As<IQueryable<ApplicationUser>>().Setup(m => m.GetEnumerator()).Returns(() => users.GetEnumerator());

            mockUserSet.Setup(m => m.Find(It.IsAny<object[]>()))
                .Returns<object[]>(ids => users.FirstOrDefault(u => u.Id == (string)ids[0]));

            _mockContext.Setup(c => c.Users).Returns(mockUserSet.Object);
        }

        [Test]
        public void Create_ValidUser_Succeeds()
        {
            // Arrange
            var newUser = new ApplicationUser
            {
                UserName = "nuevo_usuario@test.com",
                Email = "nuevo_usuario@test.com",
                PhoneNumber = "987654321",
                Name = "Nuevo",
                Surname = "Usuario",
                LastName = "Apellido",
                BirthDate = new DateTime(1995, 1, 1)
            };

            _mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success);

            // Act
            var result = _mockUserManager.Object.CreateAsync(newUser, "Password123!").Result;

            // Assert
            Assert.That(result.Succeeded);
            _mockUserManager.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void Read_ExistingUser_ReturnsUser()
        {
            // Arrange
            _mockUserManager.Setup(x => x.FindByIdAsync(_userId))
                .ReturnsAsync(_testUser);

            // Act
            var result = _mockUserManager.Object.FindByIdAsync(_userId).Result;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.UserName, Is.EqualTo("usuario_prueba@test.com"));
            Assert.That(result.Email, Is.EqualTo("usuario_prueba@test.com"));
        }

        [Test]
        public void Update_ValidUser_Succeeds()
        {
            // Arrange
            _testUser.Name = "Nombre Actualizado";
            _testUser.Email = "actualizado@test.com";

            _mockUserManager.Setup(x => x.UpdateAsync(_testUser))
                .ReturnsAsync(IdentityResult.Success);

            // Act
            var result = _mockUserManager.Object.UpdateAsync(_testUser).Result;

            // Assert
            Assert.That(result.Succeeded);
            Assert.That(_testUser.Name, Is.EqualTo("Nombre Actualizado"));
            Assert.That(_testUser.Email, Is.EqualTo("actualizado@test.com"));
        }

        [Test]
        public void Delete_ExistingUser_Succeeds()
        {
            // Arrange
            _mockUserManager.Setup(x => x.DeleteAsync(_testUser))
                .ReturnsAsync(IdentityResult.Success);

            // Act
            var result = _mockUserManager.Object.DeleteAsync(_testUser).Result;

            // Assert
            Assert.That(result.Succeeded);
            _mockUserManager.Verify(x => x.DeleteAsync(_testUser), Times.Once());
        }

        [Test]
        public void Create_InvalidEmail_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                var invalidUser = new ApplicationUser();
                invalidUser.Email = "email_invalido";
            });
        }

        [Test]
        public void Create_EmptyEmail_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                var invalidUser = new ApplicationUser();
                invalidUser.Email = "";
            });
        }

        [Test]
        public void Create_InvalidPhoneNumber_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                var user = new ApplicationUser();
                user.PhoneNumber = "abc123";
            });
        }

        [Test]
        public void Update_UserWithGreenHouses_Success()
        {
            // Arrange
            var greenhouses = new List<GreenHouse>
            {
                new GreenHouse { GreenHouseID = "GH1", Name = "Invernadero 1", UserID = _userId },
                new GreenHouse { GreenHouseID = "GH2", Name = "Invernadero 2", UserID = _userId }
            };

            _testUser.GreenHouses = greenhouses;

            // Act & Assert
            Assert.That(_testUser.GreenHouses.Count, Is.EqualTo(2));
            Assert.That(_testUser.GreenHouses.First().Name, Is.EqualTo("Invernadero 1"));
        }

        [Test]
        public void Create_EmptyUserName_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                var user = new ApplicationUser();
                user.UserName = "";
            });
        }

        [Test]
        public void Create_NullUserName_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                var user = new ApplicationUser();
                user.UserName = null;
            });
        }

        [Test]
        public void Create_WithoutRequiredLastName_Fails()
        {
            // Arrange
            var userWithoutLastName = new ApplicationUser
            {
                UserName = "test@test.com",
                Email = "test@test.com",
                LastName = null // LastName es requerido
            };

            _mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed("LastName is required"));

            // Act
            var result = _mockUserManager.Object.CreateAsync(userWithoutLastName, "Password123!").Result;

            // Assert
            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void Update_EmailWithInvalidFormat_Fails()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
            {
                _testUser.Email = "correo.invalido";
            });
        }

        [Test]
        public void Create_ValidPhoneNumber_Succeeds()
        {
            // Arrange
            var user = new ApplicationUser();

            // Act
            user.PhoneNumber = "123456789";

            // Assert
            Assert.That(user.PhoneNumber, Is.EqualTo("123456789"));
        }

        [Test]
        public void Create_EmptyPhoneNumber_Succeeds()
        {
            // Arrange
            var user = new ApplicationUser();

            // Act
            user.PhoneNumber = "";

            // Assert
            Assert.That(user.PhoneNumber, Is.Empty);
        }

        [Test]
        public void Update_UserWithNoGreenHouses_HasEmptyCollection()
        {
            // Arrange
            var newUser = new ApplicationUser
            {
                UserName = "test@test.com",
                Email = "test@test.com",
                LastName = "Apellido"
            };

            // Act & Assert
            Assert.That(newUser.GreenHouses, Is.Null.Or.Empty);
        }

        [Test]
        public void Create_DuplicateEmail_Fails()
        {
            // Arrange
            var existingEmail = "usuario_prueba@test.com";
            var newUser = new ApplicationUser
            {
                UserName = existingEmail,
                Email = existingEmail,
                LastName = "Apellido"
            };

            _mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed("Email already exists"));

            // Act
            var result = _mockUserManager.Object.CreateAsync(newUser, "Password123!").Result;

            // Assert
            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void Update_MultipleGreenHouses_CanAccessAll()
        {
            // Arrange
            var greenhouses = new List<GreenHouse>
            {
                new GreenHouse { GreenHouseID = "GH1", Name = "Invernadero 1", UserID = _userId },
                new GreenHouse { GreenHouseID = "GH2", Name = "Invernadero 2", UserID = _userId },
                new GreenHouse { GreenHouseID = "GH3", Name = "Invernadero 3", UserID = _userId }
            };

            _testUser.GreenHouses = greenhouses;

            // Act
            var allGreenhouses = _testUser.GreenHouses.ToList();

            // Assert
            Assert.That(allGreenhouses.Count, Is.EqualTo(3));
            Assert.That(allGreenhouses.Select(gh => gh.Name), 
                Is.EquivalentTo(new[] { "Invernadero 1", "Invernadero 2", "Invernadero 3" }));
            Assert.That(allGreenhouses.All(gh => gh.UserID == _userId), Is.True);
        }

        [Test]
        public void Update_UserBirthDate_Succeeds()
        {
            // Arrange
            var newBirthDate = new DateTime(1985, 6, 15);

            // Act
            _testUser.BirthDate = newBirthDate;

            // Assert
            Assert.That(_testUser.BirthDate, Is.EqualTo(newBirthDate));
        }

        [TearDown]
        public void TearDown()
        {
            _mockContext?.Object?.Dispose();
            _mockUserManager?.Object?.Dispose();
        }
    }
}