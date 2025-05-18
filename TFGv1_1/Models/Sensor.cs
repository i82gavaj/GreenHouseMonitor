using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Web;
using TFGv1_1.Models;

namespace TFGv1_1.Models
{
    public enum SensorType
    {
        Temperature, CO2, Brightness, Humidity
    }
    public enum Units
    {
        GCelsius, Lumen, microgm3, porcentaje
    }
    public class Sensor
    {
        [Key]
        public int SensorID { get; set; }

        [Required]
        [StringLength(15)]
        public string SensorName { get; set; }
        
        [Required]
        public SensorType SensorType { get; set; }

        [Required]
        public Units Units { get; set; }

        [Required]
        [StringLength(120)]     
        public string Topic { get; set; }

        [Required]
        [ForeignKey("GreenHouse")]
        public string GreenHouseID { get; set; }

        // Relaciones
        public virtual GreenHouse GreenHouse { get; set; }
        public virtual SensorLogFile LogFile { get; set; }
        public virtual ICollection<SensorLogFile> SensorLogFiles { get; set; }
        public virtual ICollection<Alert> Alerts { get; set; }
    }
}