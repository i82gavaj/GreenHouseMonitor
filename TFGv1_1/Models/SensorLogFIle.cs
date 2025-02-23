using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace TFGv1_1.Models
{
    public class SensorLogFile
    {
        [Key]
        [ForeignKey("Sensor")]
        public int SensorId { get; set; }  // Ahora es tanto PK como FK

        [Required]
        [StringLength(255)]
        public string FilePath { get; set; }

        [Required]
        public DateTime CreationDate { get; set; }

        public int LogFileId { get; set; }  // Añadir esta propiedad si no existe

        // Relación 1:1 con Sensor
        public virtual Sensor Sensor { get; set; }
    }
}