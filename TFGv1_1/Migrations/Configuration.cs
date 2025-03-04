namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Migrations;
    using System.Linq;
    using System.Collections.Generic;
    using TFGv1_1.Models;
    using Microsoft.AspNet.Identity;
    using Microsoft.AspNet.Identity.EntityFramework;

    internal sealed class Configuration : DbMigrationsConfiguration<TFGv1_1.Models.ApplicationDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(TFGv1_1.Models.ApplicationDbContext context)
        {
            // Crear roles si no existen
            var roleManager = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(context));
            
            if (!roleManager.RoleExists("Usuario"))
            {
                roleManager.Create(new IdentityRole("Usuario"));
            }
            
            // Configurar UserManager para asignar roles
            var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(context));
            
            // Modificar temporalmente los requisitos de contraseña para permitir contraseñas simples
            userManager.PasswordValidator = new PasswordValidator
            {
                RequiredLength = 4,                 // Reducir longitud mínima a 4
                RequireNonLetterOrDigit = false,    // No requerir caracteres especiales
                RequireDigit = false,               // No requerir dígitos
                RequireLowercase = false,           // No requerir minúsculas
                RequireUppercase = false,           // No requerir mayúsculas
            };
            
            // Lista de usuarios a crear
            var users = new List<(string Email, string Password, string Name, string Surname, string LastName)>
            {
                ("maria@ejemplo.com", "maria123", "María", "García", "López"),
                ("juan@ejemplo.com", "juan123", "Juan", "Martínez", "Rodríguez"),
                ("ana@ejemplo.com", "ana123", "Ana", "Fernández", "Sánchez"),
                ("carlos@ejemplo.com", "carlos123", "Carlos", "González", "Pérez"),
                ("laura@ejemplo.com", "laura123", "Laura", "Díaz", "Gómez")
            };
            
            // Crear cada usuario
            foreach (var userInfo in users)
            {
                CreateUserWithGreenhouseAndSensors(context, userManager, userInfo.Email, userInfo.Password, 
                    userInfo.Name, userInfo.Surname, userInfo.LastName);
            }
        }
        
        // Método para crear un usuario con invernaderos y sensores
        private void CreateUserWithGreenhouseAndSensors(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager, 
            string email, 
            string password, 
            string name, 
            string surname, 
            string lastName)
        {
            if (!context.Users.Any(u => u.Email == email))
            {
                // Crear el usuario
                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    Name = name,
                    Surname = surname,
                    LastName = lastName,
                    BirthDate = new DateTime(1990, 1, 1).AddDays(new Random().Next(365 * 10)), // Fecha aleatoria
                    PhoneNumber = $"6{new Random().Next(10000000, 99999999)}", // Teléfono aleatorio
                    EmailConfirmed = true
                };
                
                var result = userManager.Create(user, password);
                
                if (result.Succeeded)
                {
                    // Asignar rol al usuario
                    userManager.AddToRole(user.Id, "Usuario");
                    
                    System.Diagnostics.Debug.WriteLine($"Usuario creado con éxito - INICIAR SESIÓN CON: Email: {email} / Contraseña: {password}");
                    
                    // Crear invernaderos para el usuario (entre 1 y 3)
                    int numGreenhouses = new Random().Next(1, 4);
                    for (int i = 1; i <= numGreenhouses; i++)
                    {
                        CreateGreenhouseWithSensors(context, user.Id, $"Invernadero de {name} {i}", i);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Error al crear usuario {email}:");
                    foreach (var error in result.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine(error);
                    }
                }
            }
        }
        
        // Método para crear un invernadero con sensores
        private void CreateGreenhouseWithSensors(
            ApplicationDbContext context, 
            string userId, 
            string name, 
            int index)
        {
            // Crear un invernadero
            var greenhouse = new GreenHouse
            {
                GreenHouseID = $"GH_{Guid.NewGuid().ToString().Substring(0, 8)}",
                UserID = userId,
                Name = name,
                Description = $"Descripción del invernadero {index}",
                Location = GetRandomLocation(),
                Area = 50.0f + (new Random().Next(1, 10) * 50.0f) // Área entre 50 y 500 m²
            };
            
            context.GreenHouses.AddOrUpdate(g => g.Name, greenhouse);
            context.SaveChanges();
            
            // Crear sensores para el invernadero (entre 2 y 5)
            int numSensors = new Random().Next(2, 6);
            CreateSensorsForGreenhouse(context, greenhouse, numSensors);
        }
        
        // Método para crear sensores para un invernadero
        private void CreateSensorsForGreenhouse(
            ApplicationDbContext context, 
            GreenHouse greenhouse, 
            int numSensors)
        {
            // Tipos de sensores disponibles
            var sensorTypes = new[]
            {
                (SensorType.Temperature, Units.GCelsius, "Temperatura"),
                (SensorType.Humidity, Units.gm3, "Humedad"),
                (SensorType.CO2, Units.microgm3, "CO2"),
                (SensorType.Brightness, Units.Lumen, "Luminosidad")
            };
            
            // Crear sensores aleatorios
            var random = new Random();
            for (int i = 0; i < numSensors; i++)
            {
                // Seleccionar un tipo de sensor aleatorio
                var sensorTypeInfo = sensorTypes[random.Next(sensorTypes.Length)];
                
                // Crear el sensor
                var sensor = new Sensor
                {
                    SensorName = $"{sensorTypeInfo.Item3} {i+1}",
                    SensorType = sensorTypeInfo.Item1,
                    Units = sensorTypeInfo.Item2,
                    Topic = $"{greenhouse.GreenHouseID}/{sensorTypeInfo.Item3.ToLower()}{i+1}",
                    GreenHouseID = greenhouse.GreenHouseID
                };
                
                context.Sensors.AddOrUpdate(s => new { s.SensorName, s.GreenHouseID }, sensor);
                context.SaveChanges();
                
                // Crear archivo de log para el sensor
                var logFile = new SensorLogFile
                {
                    SensorId = sensor.SensorID,
                    FilePath = $"logs/{greenhouse.GreenHouseID}/{sensorTypeInfo.Item3.ToLower()}{i+1}.log",
                    CreationDate = DateTime.Now.AddDays(-random.Next(1, 30)), // Fecha aleatoria en el último mes
                    LogFileId = sensor.SensorID
                };
                
                context.SensorLogFiles.AddOrUpdate(l => l.SensorId, logFile);
                context.SaveChanges();
                
                System.Diagnostics.Debug.WriteLine($"Sensor {sensor.SensorName} creado para el invernadero {greenhouse.Name}");
            }
        }
        
        // Método para obtener una ubicación aleatoria
        private string GetRandomLocation()
        {
            string[] locations = {
                "Madrid, España", 
                "Barcelona, España", 
                "Valencia, España", 
                "Sevilla, España", 
                "Zaragoza, España",
                "Málaga, España",
                "Murcia, España",
                "Palma de Mallorca, España",
                "Las Palmas, España",
                "Bilbao, España",
                "Alicante, España",
                "Córdoba, España",
                "Valladolid, España",
                "Vigo, España",
                "Gijón, España"
            };
            
            return locations[new Random().Next(locations.Length)];
        }
    }
}
