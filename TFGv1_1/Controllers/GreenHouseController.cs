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
    public class GreenHouseController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: GreenHouse
        public ActionResult Index(int? page)
        {
            var userId = User.Identity.GetUserId();
            var greenHouses = db.GreenHouses
                .Where(g => g.UserID == userId)
                .OrderBy(g => g.Name);
                
            int pageSize = 10; // Número de elementos por página
            int pageNumber = (page ?? 1); // Si page es null, usar 1 como valor predeterminado
            
            // Configurar valores para la paginación manual
            int totalItems = greenHouses.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            
            return View(greenHouses.ToPagedList(pageNumber, pageSize));
        }

        // GET: GreenHouse/Details/5
        public ActionResult Details(string id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            GreenHouse greenHouse = db.GreenHouses.Find(id);
            if (greenHouse == null)
            {
                return HttpNotFound();
            }
            return View(greenHouse);
        }

        // GET: GreenHouse/Create
        public ActionResult Create()
        {
            var userId = User.Identity.GetUserId();
            var greenhouse = new GreenHouse 
            { 
                UserID = userId,
                GreenHouseID = $"GH_{Guid.NewGuid().ToString().Substring(0, 8)}"
            };
            return View(greenhouse);
        }

        // POST: GreenHouse/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "GreenHouseID,UserID,Name,Description,Location,Area")] GreenHouse greenHouse)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    // Verificar si el usuario ya tiene un invernadero con el mismo nombre
                    var existingGreenhouse = db.GreenHouses
                        .FirstOrDefault(g => g.UserID == greenHouse.UserID && g.Name == greenHouse.Name);
                        
                    if (existingGreenhouse != null)
                    {
                        ModelState.AddModelError("Name", "Ya tienes un invernadero con este nombre");
                        return View(greenHouse);
                    }

                    db.GreenHouses.Add(greenHouse);
                    db.SaveChanges();
                    return RedirectToAction("Index");
                }

                // Si llegamos aquí, algo falló
                foreach (var modelStateKey in ModelState.Keys)
                {
                    var modelStateVal = ModelState[modelStateKey];
                    foreach (var error in modelStateVal.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"Key: {modelStateKey}, Error: {error.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ha ocurrido un error al crear el invernadero: " + ex.Message);
            }

            return View(greenHouse);
        }

        // GET: GreenHouse/Edit/5
        public ActionResult Edit(string id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            var userId = User.Identity.GetUserId();
            GreenHouse greenHouse = db.GreenHouses
                .FirstOrDefault(g => g.GreenHouseID == id && g.UserID == userId);
            
            if (greenHouse == null)
            {
                return HttpNotFound();
            }

            return View(greenHouse);
        }

        // POST: GreenHouse/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "GreenHouseID,Name,Description,Location,Area,UserID")] GreenHouse greenHouse)
        {
            if (ModelState.IsValid)
            {
                var userId = User.Identity.GetUserId();
                var existingGreenHouse = db.GreenHouses.Find(greenHouse.GreenHouseID);
                
                if (existingGreenHouse == null || existingGreenHouse.UserID != userId)
                {
                    return HttpNotFound();
                }

                existingGreenHouse.Name = greenHouse.Name;
                existingGreenHouse.Description = greenHouse.Description;
                existingGreenHouse.Location = greenHouse.Location;
                existingGreenHouse.Area = greenHouse.Area;
                
                db.Entry(existingGreenHouse).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(greenHouse);
        }

        // GET: GreenHouse/Delete/5
        public ActionResult Delete(string id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            GreenHouse greenHouse = db.GreenHouses.Find(id);
            if (greenHouse == null)
            {
                return HttpNotFound();
            }
            return View(greenHouse);
        }

        // POST: GreenHouse/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(string id)
        {
            // Cargar el invernadero con sus sensores
            GreenHouse greenHouse = db.GreenHouses
                .Include(g => g.Sensors)
                .FirstOrDefault(g => g.GreenHouseID == id);

            if (greenHouse == null)
            {
                return HttpNotFound();
            }

            // Verificar que el usuario actual es dueño del invernadero
            var userId = User.Identity.GetUserId();
            if (greenHouse.UserID != userId)
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            // Eliminar cada sensor y sus logs asociados
            foreach (var sensor in greenHouse.Sensors.ToList())
            {
                // Cargar el archivo de log asociado al sensor
                var sensorLogFile = db.SensorLogFiles
                    .FirstOrDefault(l => l.SensorId == sensor.SensorID);

                // Eliminar el archivo físico si existe
                if (sensorLogFile != null)
                {
                    string fullPath = Path.Combine(Server.MapPath("~/Logs"), sensorLogFile.FilePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }

                    // Eliminar el registro del archivo de log
                    db.SensorLogFiles.Remove(sensorLogFile);
                }

                // Eliminar el sensor
                db.Sensors.Remove(sensor);
            }

            // Eliminar el invernadero
            db.GreenHouses.Remove(greenHouse);
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
