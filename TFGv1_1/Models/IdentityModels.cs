using System.Collections.Generic;
using System;
using System.Data.Entity;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using TFGv1_1.Models;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace TFGv1_1.Models
{
    // Para agregar datos de perfil del usuario, agregue más propiedades a su clase ApplicationUser. Visite https://go.microsoft.com/fwlink/?LinkID=317594 para obtener más información.
    public class ApplicationUser : IdentityUser
    {
        public async Task<ClaimsIdentity> GenerateUserIdentityAsync(UserManager<ApplicationUser> manager)
        {
            // Tenga en cuenta que authenticationType debe coincidir con el valor definido en CookieAuthenticationOptions.AuthenticationType
            var userIdentity = await manager.CreateIdentityAsync(this, DefaultAuthenticationTypes.ApplicationCookie);
            // Agregar reclamaciones de usuario personalizadas aquí
            return userIdentity;
        }

        //Atributos Añadidos

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }
        public DateTime BirthDate { get; set; }       
        
        // Un usuario puede tener múltiples invernaderos
        public virtual ICollection<GreenHouse> GreenHouses { get; set; }

        public string Name { get; set; }
        public string Surname { get; set; }

        public new string UserName
        {
            get => base.UserName;
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("El nombre de usuario no puede estar vacío");
                base.UserName = value;
            }
        }

        public new string Email
        {
            get => base.Email;
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("El email no puede estar vacío");
                if (!value.Contains("@") || !value.Contains("."))
                    throw new ArgumentException("El formato del email no es válido");
                base.Email = value;
            }
        }

        public new string PhoneNumber
        {
            get => base.PhoneNumber;
            set
            {
                if (!string.IsNullOrEmpty(value) && !value.All(char.IsDigit))
                    throw new ArgumentException("El número de teléfono solo puede contener dígitos");
                base.PhoneNumber = value;
            }
        }
    }

    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext()
            : base("DefaultConnection", throwIfV1Schema: false)
        {
        }

        public static ApplicationDbContext Create()
        {
            return new ApplicationDbContext();
        }

        public virtual DbSet<GreenHouse> GreenHouses { get; set; }
        public virtual DbSet<Sensor> Sensors { get; set; }
        public virtual DbSet<SensorLogFile> SensorLogFiles { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuración de la relación Sensor - SensorLogFile
            modelBuilder.Entity<Sensor>()
                .HasOptional(s => s.LogFile)
                .WithRequired(l => l.Sensor)
                .WillCascadeOnDelete(true);

            // Configuración de la relación GreenHouse - Sensor
            modelBuilder.Entity<GreenHouse>()
                .HasMany(g => g.Sensors)
                .WithRequired(s => s.GreenHouse)
                .HasForeignKey(s => s.GreenHouseID)
                .WillCascadeOnDelete(true);
        }
    }
}