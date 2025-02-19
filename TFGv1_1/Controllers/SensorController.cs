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
            var sensors = db.Sensors.Include(s => s.User);
            return View(sensors.ToList());
        }
        
        public ActionResult Simulation(int? id) 
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sensor sensor = db.Sensors.Include(s => s.User).FirstOrDefault(s => s.SensorID == id);

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
            
            Sensor sensor = db.Sensors.Include(s => s.User).FirstOrDefault(s => s.SensorID == id);
            
            if (sensor == null)
            {
                return HttpNotFound();
            }
            
            return View(sensor);
        }
        public ActionResult Graphs()
        {
            var userId = User.Identity.GetUserId();
            var userSensors = db.Sensors.Where(s => s.UserID == userId).ToList();
            return View(userSensors);
        }

        // GET: Sensor/Create
        public ActionResult Create()
        {
            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            return View();
        }

        // POST: Sensor/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "SensorID,SensorName,SensorType,Units,Topic")] Sensor sensor)
        {
            if (ModelState.IsValid && ValidateTopic(sensor.Topic, sensor.UserID))
            {
                sensor.UserID = User.Identity.GetUserId();
                db.Sensors.Add(sensor);
                db.SaveChanges();

                // Crear el archivo de log
                var sensorLogFile = new SensorLogFile { SensorId = sensor.SensorID };
                var logFileController = new SensorLogFileController();
                logFileController.ControllerContext = this.ControllerContext;
                return logFileController.Create(sensorLogFile);
            }
            
            return View(sensor);
        }
        private bool ValidateTopic(string Topic,string SensorID)
        {
            var userId = User.Identity.GetUserId();
            var existingSensor = db.Sensors.FirstOrDefault(s => s.Topic == Topic && s.UserID == userId);
            
            if (existingSensor != null) 
            {
                ModelState.AddModelError("Topic", "Este topic ya está en uso.");
                return false;
            }
            
            return true;
            
        }
        

        // GET: Sensor/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sensor sensor = db.Sensors.Find(id);
            if (sensor == null)
            {
                return HttpNotFound();
            }
            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            return View(sensor);
        }

        // POST: Sensor/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "SensorID,SensorName,SensorType,Units,Topic")] Sensor sensor)
        {
            if (ModelState.IsValid && ValidateTopic(sensor.Topic, sensor.UserID))
            {
                db.Entry(sensor).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.SensorTypes = Enum.GetValues(typeof(SensorType)).Cast<SensorType>().ToList();
            return View(sensor);
        }


        // GET: Sensor/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sensor sensor = db.Sensors.Find(id);
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
            Sensor sensor = db.Sensors.Find(id);
            if (sensor != null)
            {
                // Eliminar el archivo de log
                var logFileController = new SensorLogFileController();
                logFileController.ControllerContext = this.ControllerContext;
                logFileController.Delete(id);

                db.Sensors.Remove(sensor);
                db.SaveChanges();
            }
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
