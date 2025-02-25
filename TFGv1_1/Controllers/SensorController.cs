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

namespace TFGv1_1.Controllers
{
    [Authorize]
    public class SensorController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: Sensor
        public ActionResult Index()
        {
            var userId = User.Identity.GetUserId();
            var sensors = db.Sensors
                .Include(s => s.GreenHouse)
                .Where(s => s.GreenHouse.UserID == userId);
            return View(sensors.ToList());
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
            IEnumerable<SelectListItem> greenhousesList;
            try
            {
                var userId = User.Identity.GetUserId();
                
                if (ModelState.IsValid && ValidateTopic(sensor.Topic))
                {
                    var greenhouse = db.GreenHouses.FirstOrDefault(g => g.GreenHouseID == sensor.GreenHouseID && g.UserID == userId);
                    
                    if (greenhouse == null)
                    {
                        ModelState.AddModelError("GreenHouseID", "Invernadero no válido o no autorizado.");
                        
                        // Volver a cargar las listas
                        greenhousesList = LoadGreenHousesList(userId);
                        ViewBag.GreenHouses = greenhousesList;
                        ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                        return View(sensor);
                    }

                    try
                    {
                        // El topic ahora se forma con el ID del invernadero seleccionado
                        sensor.Topic = $"{sensor.GreenHouseID}/{sensor.Topic}";
                        
                        db.Sensors.Add(sensor);
                        
                        try
                        {
                            db.SaveChanges();
                        }
                        catch (System.Data.Entity.Validation.DbEntityValidationException ex)
                        {
                            foreach (var validationErrors in ex.EntityValidationErrors)
                            {
                                foreach (var validationError in validationErrors.ValidationErrors)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Property: {validationError.PropertyName} Error: {validationError.ErrorMessage}");
                                    ModelState.AddModelError(validationError.PropertyName, validationError.ErrorMessage);
                                }
                            }
                            
                            // Volver a cargar las listas
                            greenhousesList = LoadGreenHousesList(userId);
                            ViewBag.GreenHouses = greenhousesList;
                            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                            return View(sensor);
                        }

                        var sensorLogFile = new SensorLogFile { SensorId = sensor.SensorID };
                        var logFileController = new SensorLogFileController();
                        logFileController.ControllerContext = this.ControllerContext;
                        return logFileController.Create(sensorLogFile);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error al guardar el sensor: {ex.ToString()}");
                        ModelState.AddModelError("", $"Error al guardar el sensor: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException}");
                        }
                    }
                }
                
                // Si llegamos aquí, algo falló; volver a cargar las listas
                greenhousesList = LoadGreenHousesList(userId);
                ViewBag.GreenHouses = greenhousesList;
                ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
                return View(sensor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error general: {ex.ToString()}");
                ModelState.AddModelError("", $"Error al crear el sensor: {ex.Message}");
                
                // Volver a cargar las listas
                greenhousesList = LoadGreenHousesList(User.Identity.GetUserId());
                ViewBag.GreenHouses = greenhousesList;
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
                .Select(g => new SelectListItem
                {
                    Value = g.GreenHouseID.ToString(),
                    Text = g.Name
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
            try
            {
                if (ModelState.IsValid)
                {
                    var userId = User.Identity.GetUserId();
                    var originalSensor = db.Sensors
                        .Include(s => s.GreenHouse)
                        .FirstOrDefault(s => s.SensorID == sensor.SensorID && s.GreenHouse.UserID == userId);

                    if (originalSensor == null)
                    {
                        return HttpNotFound();
                    }

                    // Mantener el GreenHouseID original para evitar cambios no autorizados
                    sensor.GreenHouseID = originalSensor.GreenHouseID;

                    db.Entry(originalSensor).CurrentValues.SetValues(sensor);
                    db.SaveChanges();
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al editar el sensor: {ex.ToString()}");
                ModelState.AddModelError("", $"Error al editar el sensor: {ex.Message}");
            }

            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            ViewBag.Units = Enum.GetValues(typeof(Units)).Cast<Units>().ToList();
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
            Sensor sensor = db.Sensors
                .Include(s => s.LogFile)  // Incluir el LogFile relacionado
                .Include(s => s.GreenHouse)     // Incluir el GreenHouse relacionado
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
            }

            db.Sensors.Remove(sensor);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
