using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFGv1_1.Models
{
    public enum AlertType
    {
        Temperature,
        Humidity,
        CO2,
        Brightness
    }

    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class Alert
    {
        [Key]
        public int AlertID { get; set; }

        [Required]
        [ForeignKey("GreenHouse")]
        public string GreenHouseID { get; set; }

        [Required]
        [ForeignKey("Sensor")]
        public int SensorID { get; set; }

        [Required]
        public AlertType AlertType { get; set; }

        [Required]
        public AlertSeverity Severity { get; set; }

        [Required]
        public string Message { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public bool IsResolved { get; set; }

        [Required]
        [Display(Name = "Rango Umbral")]
        [RegularExpression(@"^\s*-?\d+(\.\d+)?\s*-\s*-?\d+(\.\d+)?\s*$", ErrorMessage = "El formato debe ser 'min-max', por ejemplo: 10-30")]
        public string ThresholdRange { get; set; }

        [Required]
        public double CurrentValue { get; set; }

        [Required]
        public bool NotifyByEmail { get; set; }

        [Required]
        public bool NotifyByPush { get; set; }

        [Required]
        public bool IsNotification { get; set; }

        // Relaciones
        public virtual GreenHouse GreenHouse { get; set; }
        public virtual Sensor Sensor { get; set; }
    }
} 