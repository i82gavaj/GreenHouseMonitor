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
        GCelsius, Lumen, microgm3, gm3
    }
    public class Sensor
    {
        [Required]
        public int SensorID { get; set; }

        [Required]
        [StringLength(15)]
        public string SensorName { get; set; }
        
        [Required]
        public SensorType SensorType { get; set; }

        [Required]
        public Units Units { get; set; }

        [Required]
        [StringLength(20)]     
        public string Topic { get; set; }

        [ForeignKey("User")]
        public string UserID { get; set; }
        public virtual ApplicationUser User { get; set; }

        // Relación 1:1 con SensorLogFile
        public virtual SensorLogFile LogFile { get; set; }
    }
}