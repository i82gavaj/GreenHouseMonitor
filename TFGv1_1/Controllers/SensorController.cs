using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using TFGv1_1.Models;
using Microsoft.AspNet.Identity;
using System.IO;
using PagedList;

namespace TFGv1_1.Controllers
{
    [Authorize]
    public class SensorController : Controller
    {
        private readonly ApplicationDbContext db;

        // Constructor sin parámetros para MVC
        public SensorController()
        {
            db = new ApplicationDbContext();
        }

        // Constructor para pruebas unitarias
        public SensorController(ApplicationDbContext context)
        {
            db = context ?? throw new ArgumentNullException(nameof(context));
        }

        // GET: Sensor
        public ActionResult Index(int? page)
        {
            var userId = User.Identity.GetUserId();
            var sensors = db.Sensors
                .Include(s => s.GreenHouse)
                .Where(s => s.GreenHouse.UserID == userId)
                .OrderBy(s => s.SensorName);
                
            int pageSize = 10; // Número de elementos por página
            int pageNumber = (page ?? 1); // Si page es null, usar 1 como valor predeterminado
            
            // Configurar valores para la paginación manual
            int totalItems = sensors.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            
            // Usar PagedList para la paginación
            return View(sensors.ToPagedList(pageNumber, pageSize));
        }
        
        public ActionResult Simulation(int? id) 
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sensor sensor = db.Sensors.Include(s => s.GreenHouse).FirstOrDefault(s => s.SensorID == id);

            if (sensor == null)
            {
                return HttpNotFound();
            }

            return View(sensor);
        }

        // GET: Sensor/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            Sensor sensor = db.Sensors.Include(s => s.GreenHouse).FirstOrDefault(s => s.SensorID == id);
            
            if (sensor == null)
            {
                return HttpNotFound();
            }
            
            return View(sensor);
        }
        public ActionResult Graphs()
        {
            var userId = User.Identity.GetUserId();
            var userSensors = db.Sensors
                .Include(s => s.GreenHouse)
                .Where(s => s.GreenHouse.UserID == userId)
                .ToList();

            // Crear un diccionario para mapear los IDs de invernadero a sus nombres
            var greenhouseNames = userSensors
                .Select(s => s.GreenHouse)
                .Distinct()
                .ToDictionary(g => g.GreenHouseID, g => g.Name);

            ViewBag.GreenhouseNames = greenhouseNames;
            return View(userSensors);
        }

        // GET: Sensor/Create
        public ActionResult Create()
        {
            var userId = User.Identity.GetUserId();
            var greenhouses = db.GreenHouses
                .Where(g => g.UserID == userId)
                .Select(g => new SelectListItem
                {
                    Value = g.GreenHouseID.ToString(),
                    Text = g.Name
                })
                .ToList();

            if (!greenhouses.Any())
            {
                ModelState.AddModelError("", "Debe crear un invernadero antes de añadir sensores.");
                return View();
            }

            ViewBag.GreenHouses = greenhouses;
            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            return View();
        }

        // POST: Sensor/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "SensorName,SensorType,Units,Topic,GreenHouseID")] Sensor sensor)
        {
            if (!ModelState.IsValid)
            {
                var userId = User.Identity.GetUserId();
                ViewBag.GreenHouses = LoadGreenHousesList(userId);
                ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                return View(sensor);
            }

            try
            {
                var userId = User.Identity.GetUserId();
                
                // Buscar el invernadero por ID y asegurarse de que pertenece al usuario actual
                var greenhouse = db.GreenHouses.FirstOrDefault(g => g.GreenHouseID == sensor.GreenHouseID && g.UserID == userId);
                
                if (greenhouse == null)
                {
                    ModelState.AddModelError("GreenHouseID", "Invernadero no válido o no autorizado.");
                    ViewBag.GreenHouses = LoadGreenHousesList(userId);
                    ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                    return View(sensor);
                }

                // Verificar si ya existe un sensor con el mismo nombre en el mismo invernadero
                var existingSensor = db.Sensors.FirstOrDefault(s => 
                    s.GreenHouseID == sensor.GreenHouseID && 
                    s.SensorName == sensor.SensorName);
                
                if (existingSensor != null)
                {
                    ModelState.AddModelError("SensorName", "Ya existe un sensor con este nombre en el invernadero seleccionado.");
                    ViewBag.GreenHouses = LoadGreenHousesList(userId);
                    ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                    return View(sensor);
                }

                // Construir el topic completo con el ID del invernadero
                sensor.Topic = $"{sensor.GreenHouseID}/{sensor.Topic}";
                
                // Agregar el sensor a la base de datos
                db.Sensors.Add(sensor);
                db.SaveChanges();

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error al crear el sensor: {ex.Message}");
                var userId = User.Identity.GetUserId();
                ViewBag.GreenHouses = LoadGreenHousesList(userId);
                ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                return View(sensor);
            }
        }
        private bool ValidateTopic(string Topic)
        {
            var userId = User.Identity.GetUserId();
            
            // Obtener todos los invernaderos del usuario
            var greenhouses = db.GreenHouses
                .Where(g => g.UserID == userId)
                .ToList();
            
            if (!greenhouses.Any()) return false;
            
            // Verificar si ya existe un sensor con el mismo topic en cualquiera de los invernaderos del usuario
            foreach (var greenhouse in greenhouses)
            {
                var fullTopic = $"{greenhouse.GreenHouseID}/{Topic}";
                var existingSensor = db.Sensors
                    .FirstOrDefault(s => s.Topic == fullTopic && s.GreenHouseID == greenhouse.GreenHouseID);
                
                if (existingSensor != null) 
                {
                    ModelState.AddModelError("Topic", "Este topic ya está en uso en uno de tus invernaderos.");
                    return false;
                }
            }
            
            return true;
        }
        
        private IEnumerable<SelectListItem> LoadGreenHousesList(string userId)
        {
            return db.GreenHouses
                .Where(g => g.UserID == userId)
                .OrderBy(g => g.Name)
                .Select(g => new SelectListItem
                {
                    Value = g.GreenHouseID.ToString(),
                    Text = $"{g.Name} ({g.Location}) - ID: {g.GreenHouseID}"
                })
                .ToList();
        }

        // GET: Sensor/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var userId = User.Identity.GetUserId();
            Sensor sensor = db.Sensors
                .Include(s => s.GreenHouse)
                .FirstOrDefault(s => s.SensorID == id && s.GreenHouse.UserID == userId);

            if (sensor == null)
            {
                return HttpNotFound();
            }

            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            ViewBag.Units = Enum.GetValues(typeof(Units)).Cast<Units>().ToList();
            return View(sensor);
        }

        // POST: Sensor/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "SensorID,SensorName,SensorType,Units,Topic,GreenHouseID")] Sensor sensor)
        {
            if (ModelState.IsValid)
            {
                var userId = User.Identity.GetUserId();
                var existingSensor = db.Sensors.Find(sensor.SensorID);

                if (existingSensor == null || existingSensor.GreenHouse.UserID != userId)
                {
                    return HttpNotFound();
                }

                existingSensor.SensorName = sensor.SensorName;
                existingSensor.SensorType = sensor.SensorType;
                existingSensor.Units = sensor.Units;
                existingSensor.Topic = sensor.Topic;
                existingSensor.GreenHouseID = sensor.GreenHouseID;

                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(sensor);
        }


        // GET: Sensor/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            Sensor sensor = db.Sensors
                .Include(s => s.GreenHouse.User)
                .FirstOrDefault(s => s.SensorID == id);
            
            if (sensor == null)
            {
                return HttpNotFound();
            }
            return View(sensor);
        }

        // POST: Sensor/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            // Cargar el sensor con su archivo de log y su invernadero
            Sensor sensor = db.Sensors
                .Include(s => s.LogFile)
                .Include(s => s.GreenHouse)
                .FirstOrDefault(s => s.SensorID == id);

            if (sensor == null)
            {
                return HttpNotFound();
            }

            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            // Eliminar el archivo físico si existe
            if (sensor.LogFile != null)
            {
                string fullPath = Path.Combine(Server.MapPath("~/Logs"), sensor.LogFile.FilePath);
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                // Eliminar el registro del archivo de log
                db.SensorLogFiles.Remove(sensor.LogFile);
            }

            // Eliminar el sensor
            db.Sensors.Remove(sensor);
            db.SaveChanges();
            
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}