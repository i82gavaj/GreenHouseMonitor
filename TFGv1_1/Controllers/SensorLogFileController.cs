using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using TFGv1_1.Models;
using System.IO;
using Microsoft.AspNet.Identity;
using System.Text;
using PagedList;

namespace TFGv1_1.Controllers
{
    [Authorize]
    public class SensorLogFileController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: SensorLogFile
        public ActionResult Index(int? page)
        {
            // Obtener el ID del usuario actual
            var userId = User.Identity.GetUserId();
            
            // Filtrar los logs de sensores para mostrar solo los que pertenecen a los invernaderos del usuario
            var sensorLogFiles = db.SensorLogFiles
                .Include(s => s.Sensor)
                .Include(s => s.Sensor.GreenHouse)
                .Where(s => s.Sensor.GreenHouse.UserID == userId)
                .OrderByDescending(s => s.CreationDate);
                
            int pageSize = 10; // Número de elementos por página
            int pageNumber = (page ?? 1); // Si page es null, usar 1 como valor predeterminado
            
            // Configurar valores para la paginación manual
            int totalItems = sensorLogFiles.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            
            return View(sensorLogFiles.ToPagedList(pageNumber, pageSize));
        }

        // GET: SensorLogFile/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            SensorLogFile sensorLogFile = db.SensorLogFiles
                .Include(s => s.Sensor)
                .Include(s => s.Sensor.GreenHouse)
                .FirstOrDefault(s => s.SensorId == id);
                
            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }
            
            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }
            
            return View(sensorLogFile);
        }

        // GET: SensorLogFile/Create
        public ActionResult Create()
        {
            ViewBag.SensorId = new SelectList(db.Sensors, "SensorID", "SensorName");
            return View();
        }

        // POST: SensorLogFile/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "SensorId,FilePath,CreationDate")] SensorLogFile sensorLogFile)
        {
            if (ModelState.IsValid)
            {
                var sensor = db.Sensors
                    .Include(s => s.GreenHouse)
                    .FirstOrDefault(s => s.SensorID == sensorLogFile.SensorId);

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

                // Obtener el GreenHouseID del topic (primera parte del topic)
                string greenhouseId = sensor.Topic.Split('/')[0];

                // Crear el directorio base de logs si no existe
                string baseLogDirectory = Server.MapPath("~/Logs");
                if (!Directory.Exists(baseLogDirectory))
                {
                    Directory.CreateDirectory(baseLogDirectory);
                }

                // Crear el directorio específico del invernadero usando el ID del topic
                string greenhouseDirectory = Path.Combine(baseLogDirectory, greenhouseId);
                if (!Directory.Exists(greenhouseDirectory))
                {
                    Directory.CreateDirectory(greenhouseDirectory);
                }

                // Generar el nombre del archivo usando el topic del sensor
                string fileName = sensor.Topic.Replace("/", "_") + ".log";
                sensorLogFile.FilePath = Path.Combine(greenhouseId, fileName);
                sensorLogFile.CreationDate = DateTime.Now;

                // Crear el archivo físico
                string fullPath = Path.Combine(baseLogDirectory, sensorLogFile.FilePath);
                if (!System.IO.File.Exists(fullPath))
                {
                    // Crear el archivo con un mensaje inicial
                    var initialMessage = new StringBuilder();
                    initialMessage.AppendLine("════════════════════════════════════════");
                    initialMessage.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ARCHIVO DE LOG CREADO");
                    initialMessage.AppendLine($"Invernadero: {greenhouseId}");
                    initialMessage.AppendLine($"Sensor: {sensor.SensorName}");
                    initialMessage.AppendLine($"Topic: {sensor.Topic}");
                    initialMessage.AppendLine($"Tipo: {sensor.SensorType}");
                    initialMessage.AppendLine($"Unidades: {sensor.Units}");
                    initialMessage.AppendLine("════════════════════════════════════════");

                    System.IO.File.WriteAllText(fullPath, initialMessage.ToString());
                }

                db.SensorLogFiles.Add(sensorLogFile);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.SensorId = new SelectList(db.Sensors, "SensorID", "SensorName", sensorLogFile.SensorId);
            return View(sensorLogFile);
        }

        // GET: SensorLogFile/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            SensorLogFile sensorLogFile = db.SensorLogFiles
                .Include(s => s.Sensor)
                .Include(s => s.Sensor.GreenHouse)
                .FirstOrDefault(s => s.SensorId == id);
                
            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }
            
            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }
            
            ViewBag.SensorId = new SelectList(db.Sensors, "SensorID", "SensorName", sensorLogFile.SensorId);
            return View(sensorLogFile);
        }

        // POST: SensorLogFile/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "SensorId,FilePath,CreationDate")] SensorLogFile sensorLogFile)
        {
            if (ModelState.IsValid)
            {
                // Verificar que el usuario actual es dueño del invernadero
                var userId = User.Identity.GetUserId();
                var sensor = db.Sensors
                    .Include(s => s.GreenHouse)
                    .FirstOrDefault(s => s.SensorID == sensorLogFile.SensorId);
                
                if (sensor == null)
                {
                    return HttpNotFound();
                }
                
                if (sensor.GreenHouse.UserID != userId)
                {
                    return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
                }
                
                db.Entry(sensorLogFile).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.SensorId = new SelectList(db.Sensors, "SensorID", "SensorName", sensorLogFile.SensorId);
            return View(sensorLogFile);
        }

        // GET: SensorLogFile/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            SensorLogFile sensorLogFile = db.SensorLogFiles
                .Include(s => s.Sensor)
                .Include(s => s.Sensor.GreenHouse)
                .FirstOrDefault(s => s.SensorId == id);
                
            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }
            
            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }
            
            return View(sensorLogFile);
        }

        // POST: SensorLogFile/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            SensorLogFile sensorLogFile = db.SensorLogFiles.Include(s => s.Sensor.GreenHouse)
                                                  .FirstOrDefault(s => s.LogFileId == id);
            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }

            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            // Eliminar el archivo físico
            string fullPath = Path.Combine(Server.MapPath("~/Logs"), sensorLogFile.FilePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }

            db.SensorLogFiles.Remove(sensorLogFile);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        // GET: SensorLogFile/ViewContent/5
        public ActionResult ViewContent(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            SensorLogFile sensorLogFile = db.SensorLogFiles
                .Include(s => s.Sensor.GreenHouse)
                .FirstOrDefault(s => s.SensorId == id);

            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }

            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            // Construir la ruta del archivo usando la estructura de carpetas correcta
            string baseDirectory = Server.MapPath("~/Logs");
            string greenhouseId = sensorLogFile.Sensor.GreenHouseID;
            string logFileName = $"{greenhouseId}_{sensorLogFile.Sensor.Topic.Split('/')[1]}.log";
            string fullPath = Path.Combine(baseDirectory, greenhouseId, logFileName);
            
            ViewBag.RutaArchivo = fullPath;

            if (!System.IO.File.Exists(fullPath))
            {
                ViewBag.LogContent = $"El archivo de log está vacío o no existe. Ruta buscada: {fullPath}";
            }
            else
            {
                try 
                {
                    ViewBag.LogContent = System.IO.File.ReadAllText(fullPath);
                }
                catch (Exception ex)
                {
                    ViewBag.LogContent = $"Error al leer el archivo: {ex.Message}";
                }
            }

            return View(sensorLogFile);
        }

        // GET: SensorLogFile/GetLogContent/5
        public ActionResult GetLogContent(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            SensorLogFile sensorLogFile = db.SensorLogFiles
                .Include(s => s.Sensor.GreenHouse)
                .FirstOrDefault(s => s.SensorId == id);

            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }

            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (sensorLogFile.Sensor.GreenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            string baseDirectory = Server.MapPath("~/Logs");
            string greenhouseId = sensorLogFile.Sensor.GreenHouseID;
            string logFileName = $"{greenhouseId}_{sensorLogFile.Sensor.Topic.Split('/')[1]}.log";
            string fullPath = Path.Combine(baseDirectory, greenhouseId, logFileName);

            if (!System.IO.File.Exists(fullPath))
            {
                return Content("El archivo de log está vacío o no existe.");
            }

            try 
            {
                string content = System.IO.File.ReadAllText(fullPath);
                return Content(content);
            }
            catch (Exception ex)
            {
                return Content($"Error al leer el archivo: {ex.Message}");
            }
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
