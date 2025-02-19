using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TFGv1_1.Models
{
    public class GreenHouse
    {
        [Key]
        [Required]
        [StringLength(128)]
        public string GreenHouseID { get; set; }
        
        [Required]
        [StringLength(128)]
        [ForeignKey("User")]
        public string UserID { get; set; }
        
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(50, ErrorMessage = "El nombre no puede tener más de 50 caracteres")]
        public string Name { get; set; }

        [StringLength(200, ErrorMessage = "La descripción no puede tener más de 200 caracteres")]
        public string Description { get; set; }

        [Required(ErrorMessage = "La ubicación es obligatoria")]
        [StringLength(100, ErrorMessage = "La ubicación no puede tener más de 100 caracteres")]
        public string Location { get; set; }

        [Required(ErrorMessage = "El área es obligatoria")]
        [Range(1, 10000, ErrorMessage = "El área debe estar entre 1 y 10000 m²")]
        public float Area { get; set; }
        
        //Relacion N:1 con los usuarios (un usuario puede tener varios invernaderos)
        public virtual ApplicationUser User { get; set; }

        public virtual ICollection<Sensor> Sensors { get; set; }
    }
}