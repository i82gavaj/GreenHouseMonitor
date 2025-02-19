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

namespace TFGv1_1.Controllers
{
    [Authorize]
    public class SensorLogFileController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: SensorLogFile
        public ActionResult Index()
        {
            var sensorLogFiles = db.SensorLogFiles.Include(s => s.Sensor);
            return View(sensorLogFiles.ToList());
        }

        // GET: SensorLogFile/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            SensorLogFile sensorLogFile = db.SensorLogFiles.Find(id);
            if (sensorLogFile == null)
            {
                return HttpNotFound();
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

                // Generar el nombre del archivo usando GreenHouseID y topic del sensor
                sensorLogFile.FilePath = $"{sensor.GreenHouseID}_{sensor.Topic.Replace("/", "_")}.log";
                sensorLogFile.CreationDate = DateTime.Now;

                // Crear el archivo físico
                string logDirectory = Server.MapPath("~/Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string fullPath = Path.Combine(logDirectory, sensorLogFile.FilePath);
                System.IO.File.Create(fullPath).Close();

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
            SensorLogFile sensorLogFile = db.SensorLogFiles.Find(id);
            if (sensorLogFile == null)
            {
                return HttpNotFound();
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
            SensorLogFile sensorLogFile = db.SensorLogFiles.Find(id);
            if (sensorLogFile == null)
            {
                return HttpNotFound();
            }
            return View(sensorLogFile);
        }

        // POST: SensorLogFile/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            SensorLogFile sensorLogFile = db.SensorLogFiles.Include(s => s.Sensor.GreenHouse)
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

            // Construir el nombre del archivo usando GreenHouseID y topic del sensor
            string fileName = $"{sensorLogFile.Sensor.GreenHouseID}_{sensorLogFile.Sensor.Topic.Replace("/", "_")}.log";
            string fullPath = Path.Combine(Server.MapPath("~/Logs"), fileName);
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
