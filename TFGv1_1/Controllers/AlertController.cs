using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using TFGv1_1.Models;
using Microsoft.AspNet.Identity;
using System.Data.Entity;
using System.Net.Mail;
using System.Net;
using System.Collections.Generic;
using System.Web;

namespace TFGv1_1.Controllers
{
    [Authorize]
    public class AlertController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PageSize = 10;

        public AlertController()
        {
            _context = new ApplicationDbContext();
        }

        public async Task<ActionResult> Index(string searchString, string sortOrder, int? page, bool? showResolved)
        {
            try
            {
                var userId = User.Identity.GetUserId();
                ViewBag.CurrentSort = sortOrder;
                ViewBag.CurrentSearch = searchString;
                ViewBag.ShowResolved = showResolved ?? false;

                var alerts = _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .Where(a => a.GreenHouse.UserID == userId);

                // Filtro de búsqueda
                if (!string.IsNullOrEmpty(searchString))
                {
                    alerts = alerts.Where(a =>
                        a.GreenHouse.Name.Contains(searchString) ||
                        a.Sensor.SensorName.Contains(searchString) ||
                        a.Message.Contains(searchString));
                }

                // Filtro de resolución - Modificado para mostrar todas las alertas por defecto
                if (showResolved.HasValue && !showResolved.Value)
                {
                    alerts = alerts.Where(a => !a.IsResolved);
                }

                // Ordenamiento
                switch (sortOrder)
                {
                    case "date_desc":
                        alerts = alerts.OrderByDescending(a => a.CreatedAt);
                        break;
                    case "severity":
                        alerts = alerts.OrderBy(a => a.Severity);
                        break;
                    case "severity_desc":
                        alerts = alerts.OrderByDescending(a => a.Severity);
                        break;
                    case "type":
                        alerts = alerts.OrderBy(a => a.AlertType);
                        break;
                    case "type_desc":
                        alerts = alerts.OrderByDescending(a => a.AlertType);
                        break;
                    default:
                        alerts = alerts.OrderByDescending(a => a.CreatedAt);
                        break;
                }

                // Paginación
                var pageNumber = page ?? 1;
                var totalItems = await alerts.CountAsync();
                var totalPages = (int)Math.Ceiling(totalItems / (double)PageSize);

                var pagedAlerts = await alerts
                    .Skip((pageNumber - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                ViewBag.TotalPages = totalPages;
                ViewBag.CurrentPage = pageNumber;

                return View(pagedAlerts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Index: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                TempData["Error"] = "Error al cargar las alertas: " + ex.Message;
                return View(new List<Alert>());
            }
        }

        public async Task<ActionResult> Details(int? id)
        {
            try
            {
                if (id == null)
                {
                    return new HttpStatusCodeResult(System.Net.HttpStatusCode.BadRequest);
                }

                var userId = User.Identity.GetUserId();
                var alert = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .FirstOrDefaultAsync(a => a.AlertID == id && a.GreenHouse.UserID == userId);

                if (alert == null)
                {
                    return HttpNotFound();
                }

                return View(alert);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al cargar los detalles de la alerta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Resolve(int id)
        {
            try
            {
                var userId = User.Identity.GetUserId();
                var alert = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .FirstOrDefaultAsync(a => a.AlertID == id && a.GreenHouse.UserID == userId);

                if (alert == null)
                {
                    return HttpNotFound();
                }

                // Marcar la alerta como resuelta
                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.Now;
                
                // Si es una notificación, registrar en el log que fue resuelta manualmente
                if (alert.IsNotification)
                {
                    System.Diagnostics.Debug.WriteLine($"Notificación ID {alert.AlertID} resuelta manualmente por el usuario {userId}");
                }
                
                await _context.SaveChangesAsync();

                TempData["Success"] = "Alerta marcada como resuelta correctamente.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Resolve: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                TempData["Error"] = "Error al resolver la alerta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        private async Task SendEmailNotification(Alert alert)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == alert.GreenHouse.UserID);
                if (user == null || string.IsNullOrEmpty(user.Email)) return;

                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential("tu-email@gmail.com", "tu-contraseña"),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress("tu-email@gmail.com"),
                    Subject = $"Alerta en {alert.GreenHouse?.Name} - {alert.AlertType}",
                    Body = $@"
                        <h2>Alerta de {alert.AlertType}</h2>
                        <p><strong>Invernadero:</strong> {alert.GreenHouse.Name}</p>
                        <p><strong>Sensor:</strong> {alert.Sensor.SensorName}</p>
                        <p><strong>Severidad:</strong> {alert.Severity}</p>
                        <p><strong>Mensaje:</strong> {alert.Message}</p>
                        <p><strong>Valor actual:</strong> {alert.CurrentValue} {alert.Sensor.Units}</p>
                        <p><strong>Umbral:</strong> {alert.ThresholdRange}</p>
                        <p><strong>Fecha:</strong> {alert.CreatedAt}</p>
                    ",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(user.Email);
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al enviar email: {ex.Message}");
            }
        }

        private async Task SendPushNotification(Alert alert)
        {
            // Implementar lógica de notificaciones push aquí
            await Task.CompletedTask;
        }

        private string GetDefaultThreshold(SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.Temperature:
                    return "10-35";
                case SensorType.Humidity:
                    return "30-80";
                case SensorType.CO2:
                    return "1000-2000";
                case SensorType.Brightness:
                    return "1000-12000";
                default:
                    return "0-0";
            }
        }


        // GET: Alert/Create
        public ActionResult Create(int? sensorId)
        {
            var userId = User.Identity.GetUserId();
            var greenhouses = _context.GreenHouses.Where(g => g.UserID == userId).ToList();
            
            if (sensorId.HasValue)
            {
                var sensor = _context.Sensors
                    .Include(s => s.GreenHouse)
                    .FirstOrDefault(s => s.SensorID == sensorId && s.GreenHouse.UserID == userId);
                
                if (sensor != null)
                {
                    ViewBag.GreenHouseID = new SelectList(greenhouses, "GreenHouseID", "Name", sensor.GreenHouseID);
                    ViewBag.SensorID = new SelectList(new[] { sensor }, "SensorID", "SensorName", sensor.SensorID);
                    
                    // Establecer el tipo de alerta según el tipo de sensor
                    var alertType = (AlertType)sensor.SensorType;
                    ViewBag.AlertType = new SelectList(new[] { alertType }, alertType);
                    ViewBag.SensorType = (int)sensor.SensorType;
                    
                    ViewBag.Severity = new SelectList(Enum.GetValues(typeof(AlertSeverity)));
                    return View();
                }
            }

            var firstGreenhouseId = greenhouses.FirstOrDefault()?.GreenHouseID;
            var sensores = string.IsNullOrEmpty(firstGreenhouseId)
                ? new List<Sensor>()
                : _context.Sensors.Where(s => s.GreenHouseID == firstGreenhouseId).ToList();
            
            ViewBag.GreenHouseID = new SelectList(greenhouses, "GreenHouseID", "Name");
            ViewBag.SensorID = new SelectList(sensores, "SensorID", "SensorName");
            ViewBag.AlertType = new SelectList(Enum.GetValues(typeof(AlertType)));
            ViewBag.Severity = new SelectList(Enum.GetValues(typeof(AlertSeverity)));
            
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "GreenHouseID,SensorID,AlertType,Severity,Message,ThresholdRange,NotifyByEmail,NotifyByPush")] Alert alert)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var userId = User.Identity.GetUserId();
                    var sensor = _context.Sensors
                        .Include(s => s.GreenHouse)
                        .FirstOrDefault(s => s.SensorID == alert.SensorID && s.GreenHouse.UserID == userId);

                    if (sensor == null)
                    {
                        return new HttpStatusCodeResult(System.Net.HttpStatusCode.BadRequest);
                    }
                    
                    // Asegurarse de que la severidad es correcta (un valor entero del enum AlertSeverity)
                    int severityValue = (int)alert.Severity;
                    if (!Enum.IsDefined(typeof(AlertSeverity), severityValue))
                    {
                        // Si no es un valor válido, establecer un valor por defecto
                        alert.Severity = AlertSeverity.Medium;
                    }

                    alert.CreatedAt = DateTime.Now;
                    alert.IsResolved = false;
                    alert.IsNotification = false;
                    
                    // Configurar opciones de notificación
                    alert.NotifyByEmail = false; // Desactivar notificaciones por email
                    alert.NotifyByPush = true;   // Activar notificaciones en la interfaz
                    
                    _context.Alerts.Add(alert);
                    _context.SaveChanges();

                    TempData["Success"] = "Alerta creada correctamente.";
                    return RedirectToAction("Index");
                }

                var userId2 = User.Identity.GetUserId();
                ViewBag.GreenHouseID = new SelectList(_context.GreenHouses.Where(g => g.UserID == userId2), "GreenHouseID", "Name", alert.GreenHouseID);
                
                var sensor2 = _context.Sensors.FirstOrDefault(s => s.SensorID == alert.SensorID);
                ViewBag.SensorID = new SelectList(_context.Sensors.Where(s => s.GreenHouseID == alert.GreenHouseID), "SensorID", "SensorName", alert.SensorID);
                
                if (sensor2 != null)
                {
                    ViewBag.AlertType = new SelectList(new[] { (AlertType)sensor2.SensorType }, (AlertType)sensor2.SensorType);
                    ViewBag.SensorType = (int)sensor2.SensorType;
                }
                else
                {
                    ViewBag.AlertType = new SelectList(Enum.GetValues(typeof(AlertType)));
                }
                
                ViewBag.Severity = new SelectList(Enum.GetValues(typeof(AlertSeverity)), alert.Severity);
                return View(alert);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al crear la alerta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // AJAX: Obtener sensores por tipo
        [HttpGet]
        public JsonResult GetSensoresPorTipo(int alertType)
        {
            var userId = User.Identity.GetUserId();
            var sensores = _context.Sensors.Include(s => s.GreenHouse)
                .Where(s => s.GreenHouse.UserID == userId && (int)s.SensorType == alertType)
                .Select(s => new { s.SensorID, s.SensorName })
                .ToList();
            return Json(sensores, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> GetUnresolvedAlerts()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                var alerts = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .Where(a => a.GreenHouse.UserID == userId && !a.IsResolved && !a.IsNotification)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(20) // Aumentamos el límite para mostrar más alertas
                    .ToListAsync();

                System.Diagnostics.Debug.WriteLine($"Alertas encontradas: {alerts.Count}");

                return Json(alerts.Select(a => new
                {
                    a.AlertID,
                    a.AlertType,
                    a.Severity,
                    GreenHouseName = a.GreenHouse.Name,
                    SensorName = a.Sensor != null ? a.Sensor.SensorName : "Desconocido",
                    a.Message,
                    a.IsResolved,
                    CreatedAt = a.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    SeverityClass = GetSeverityClass(a.Severity),
                    TypeIcon = GetAlertTypeIcon(a.AlertType)
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetUnresolvedAlerts: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<ActionResult> GetUnresolvedNotifications()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                var notifications = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .Where(a => a.GreenHouse.UserID == userId && !a.IsResolved && a.IsNotification)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(20) // Aumentamos el límite para mostrar más notificaciones
                    .ToListAsync();

                System.Diagnostics.Debug.WriteLine($"Notificaciones encontradas: {notifications.Count}");

                return Json(notifications.Select(a => new
                {
                    a.AlertID,
                    a.AlertType,
                    a.Severity,
                    GreenHouseName = a.GreenHouse.Name,
                    SensorName = a.Sensor != null ? a.Sensor.SensorName : "Desconocido",
                    a.Message,
                    a.IsResolved,
                    CreatedAt = a.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    SeverityClass = GetSeverityClass(a.Severity),
                    TypeIcon = GetAlertTypeIcon(a.AlertType)
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetUnresolvedNotifications: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private string GetSeverityClass(AlertSeverity severity)
        {
            switch (severity)
            {
                case AlertSeverity.Critical:
                    return "table-danger";
                case AlertSeverity.High:
                    return "table-warning";
                case AlertSeverity.Medium:
                    return "table-info";
                case AlertSeverity.Low:
                    return "table-secondary";
                default:
                    return "";
            }
        }

        private string GetAlertTypeIcon(AlertType type)
        {
            switch (type)
            {
                case AlertType.Temperature:
                    return "fas fa-thermometer-half";
                case AlertType.Humidity:
                    return "fas fa-tint";
                case AlertType.CO2:
                    return "fas fa-wind";
                case AlertType.Brightness:
                    return "fas fa-sun";
                default:
                    return "";
            }
        }

        [HttpPost]
        public async Task<ActionResult> CheckAndCreateAlerts(string sensorTopic, double value)
        {
            try
            {
                // Usar un contexto fresco para evitar caché
                using (var freshContext = new ApplicationDbContext())
                {
                    var sensor = await freshContext.Sensors
                        .Include(s => s.GreenHouse)
                        .FirstOrDefaultAsync(s => s.Topic == sensorTopic);

                    if (sensor == null) 
                    {
                        return Json(new { success = false, message = "Sensor no encontrado" });
                    }

                    // Buscar la alerta configurada para este sensor - usar AsNoTracking para evitar caché
                    var alertConfig = await freshContext.Alerts
                        .AsNoTracking()
                        .Where(a => a.SensorID == sensor.SensorID && !a.IsNotification)
                        .OrderByDescending(a => a.CreatedAt) // Ordenar por fecha de creación para obtener la más reciente
                        .FirstOrDefaultAsync();

                    if (alertConfig == null)
                    {
                        return Json(new { success = false, message = "No hay configuración de alerta para este sensor" });
                    }

                    System.Diagnostics.Debug.WriteLine($"Alerta configurada encontrada - ID: {alertConfig.AlertID}, Severidad: {alertConfig.Severity}, Fecha: {alertConfig.CreatedAt}");

                    // Verificar si ya existe una notificación activa para este sensor
                    var existingNotification = await freshContext.Alerts
                        .FirstOrDefaultAsync(a => a.SensorID == sensor.SensorID && a.IsNotification && !a.IsResolved);

                    // Obtener el rango mínimo y máximo del umbral
                    double min = double.MinValue, max = double.MaxValue;
                    if (!string.IsNullOrEmpty(alertConfig.ThresholdRange) && alertConfig.ThresholdRange.Contains("-"))
                    {
                        var partes = alertConfig.ThresholdRange.Split('-');
                        if (partes.Length == 2)
                        {
                            if (!double.TryParse(partes[0].Trim(), out min))
                            {
                                min = double.MinValue;
                            }
                            if (!double.TryParse(partes[1].Trim(), out max))
                            {
                                max = double.MaxValue;
                            }
                        }
                    }

                    // Verificar si el valor está FUERA del rango permitido
                    if (value < min || value > max)
                    {
                        System.Diagnostics.Debug.WriteLine($"VALOR FUERA DE RANGO - Sensor: {sensor.SensorName}, Valor: {value}, Rango: {min}-{max}");
                        
                        // Si ya existe una notificación activa para este sensor, actualizarla
                        if (existingNotification != null)
                        {
                            // Actualizar el valor actual sin marcar como resuelta
                            System.Diagnostics.Debug.WriteLine($"Ya existe una notificación activa para el sensor {sensor.SensorName}, actualizando valor");
                            existingNotification.CurrentValue = value;
                            await freshContext.SaveChangesAsync();
                            System.Diagnostics.Debug.WriteLine($"Notificación existente (ID: {existingNotification.AlertID}) actualizada con nuevo valor fuera de rango: {value}");
                            return Json(new { success = true, message = "Valor actualizado en notificación existente" });
                        }
                        else
                        {
                            // Si no existe notificación activa, crear una nueva
                            var severity = alertConfig.Severity;
                            var message = GenerateAlertMessage(sensor.SensorType, value, min, max);

                            // Mapeo correcto de SensorType a AlertType
                            AlertType alertType;
                            switch (sensor.SensorType)
                            {
                                case SensorType.Temperature: // 0
                                    alertType = AlertType.Temperature; // 0
                                    break;
                                case SensorType.CO2: // 1
                                    alertType = AlertType.CO2; // 2
                                    break;
                                case SensorType.Brightness: // 2
                                    alertType = AlertType.Brightness; // 3
                                    break;
                                case SensorType.Humidity: // 3
                                    alertType = AlertType.Humidity; // 1
                                    break;
                                default:
                                    alertType = AlertType.Temperature;
                                    break;
                            }

                            var notification = new Alert
                            {
                                GreenHouseID = sensor.GreenHouseID,
                                SensorID = sensor.SensorID,
                                AlertType = alertType,
                                Severity = severity,
                                Message = message,
                                CreatedAt = DateTime.Now,
                                IsResolved = false,
                                IsNotification = true,
                                ThresholdRange = alertConfig.ThresholdRange,
                                CurrentValue = value,
                                NotifyByEmail = false,
                                NotifyByPush = true
                            };

                            freshContext.Alerts.Add(notification);
                            await freshContext.SaveChangesAsync();
                            return Json(new { success = true, message = "Alerta creada correctamente" });
                        }
                    }
                    else
                    {
                        // El valor está dentro del rango, pero NO resolvemos automáticamente
                        // Solo actualizar el valor si hay una notificación activa
                        if (existingNotification != null)
                        {
                            // Solo actualizar el valor actual SIN marcar como resuelta
                            System.Diagnostics.Debug.WriteLine($"VALOR DENTRO DE RANGO - Sensor: {sensor.SensorName}, Valor: {value}, Rango: {min}-{max}");
                            System.Diagnostics.Debug.WriteLine($"Actualizando notificación ID: {existingNotification.AlertID} sin resolver");
                            
                            // Solo actualizar el valor, no resolver
                            existingNotification.CurrentValue = value;
                            await freshContext.SaveChangesAsync();
                            
                            System.Diagnostics.Debug.WriteLine($"Notificación ID: {existingNotification.AlertID} actualizada (sin resolver)");
                            return Json(new { success = true, message = "Valor dentro del rango, notificación actualizada (requiere resolución manual)" });
                        }
                        
                        // No hay notificación activa, solo registrar que el valor está dentro del rango
                        return Json(new { success = true, message = "Valor dentro del rango permitido" });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en CheckAndCreateAlerts: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = "Error al procesar la alerta: " + ex.Message });
            }
        }

        private AlertSeverity DetermineSeverity(double value, double min, double max, SensorType sensorType)
        {
            // Calcular qué tan lejos está el valor del rango permitido
            double range = max - min;
            double deviation = Math.Max(min - value, value - max);
            double percentageDeviation = (deviation / range) * 100;

            // Determinar la severidad basada en el porcentaje de desviación
            if (percentageDeviation > 50)
                return AlertSeverity.Critical;
            else if (percentageDeviation > 30)
                return AlertSeverity.High;
            else if (percentageDeviation > 15)
                return AlertSeverity.Medium;
            else
                return AlertSeverity.Low;
        }

        private string GenerateAlertMessage(SensorType sensorType, double value, double min, double max)
        {
            string sensorName = GetSensorTypeName(sensorType);
            string unit = GetUnitForSensorType(sensorType);
            string formattedValue = FormatSensorValue(sensorType, value);
            string formattedMin = FormatSensorValue(sensorType, min);
            string formattedMax = FormatSensorValue(sensorType, max);
            
            if (value < min)
            {
                return $"El sensor de {sensorName} ha detectado un valor bajo: {formattedValue} {unit} (mínimo permitido: {formattedMin} {unit})";
            }
            else
            {
                return $"El sensor de {sensorName} ha detectado un valor alto: {formattedValue} {unit} (máximo permitido: {formattedMax} {unit})";
            }
        }

        private string GetSensorTypeName(SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.Temperature:
                    return "Temperatura";
                case SensorType.Humidity:
                    return "Humedad";
                case SensorType.CO2:
                    return "CO2";
                case SensorType.Brightness:
                    return "Luminosidad";
                default:
                    return "Desconocido";
            }
        }

        private string FormatSensorValue(SensorType sensorType, double value)
        {
            switch (sensorType)
            {
                case SensorType.Temperature:
                    return Math.Round(value, 1).ToString("0.0"); // 1 decimal para temperatura
                
                case SensorType.Humidity:
                    return Math.Round(value, 0).ToString("0"); // Sin decimales para humedad
                
                case SensorType.CO2:
                    return Math.Round(value, 0).ToString("0"); // Sin decimales para CO2
                
                case SensorType.Brightness:
                    return value >= 1000 ? 
                        Math.Round(value, 0).ToString("0") : // Sin decimales para valores altos
                        Math.Round(value, 1).ToString("0.0"); // 1 decimal para valores bajos
                
                default:
                    return Math.Round(value, 2).ToString("0.00"); // 2 decimales por defecto
            }
        }

        private string GetUnitForSensorType(SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.Temperature:
                    return "°C";
                case SensorType.Humidity:
                    return "%";
                case SensorType.CO2:
                    return "ppm";
                case SensorType.Brightness:
                    return "lux";
                default:
                    return "";
            }
        }

        // GET: Alert/Delete/5
        public async Task<ActionResult> Delete(int id)
        {
            try
            {
                var alert = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .FirstOrDefaultAsync(a => a.AlertID == id);

                if (alert == null)
                {
                    return HttpNotFound();
                }

                return View(alert);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al cargar la alerta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(id);
                _context.Alerts.Remove(alert);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Alerta eliminada correctamente";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al eliminar la alerta: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // AJAX: Obtener sensores por invernadero
        [HttpGet]
        public JsonResult GetSensoresPorInvernadero(string greenhouseId)
        {
            var userId = User.Identity.GetUserId();
            var sensores = _context.Sensors.Include(s => s.GreenHouse)
                .Where(s => s.GreenHouseID == greenhouseId && s.GreenHouse.UserID == userId)
                .Select(s => new { s.SensorID, s.SensorName })
                .ToList();
            return Json(sensores, JsonRequestBehavior.AllowGet);
        }

        // AJAX: Obtener tipo de sensor
        [HttpGet]
        public JsonResult GetSensorType(int sensorId)
        {
            var sensor = _context.Sensors.FirstOrDefault(s => s.SensorID == sensorId);
            if (sensor != null)
            {
                return Json((int)sensor.SensorType, JsonRequestBehavior.AllowGet);
            }
            return Json(0, JsonRequestBehavior.AllowGet);
        }

        // AJAX: Obtener toda la información del sensor
        [HttpGet]
        public JsonResult GetSensorInfo(int sensorId)
        {
            var sensor = _context.Sensors.FirstOrDefault(s => s.SensorID == sensorId);
            if (sensor != null)
            {
                var result = new { 
                    SensorID = sensor.SensorID,
                    SensorName = sensor.SensorName,
                    SensorType = (int)sensor.SensorType,
                    Units = sensor.Units.ToString(),
                    Topic = sensor.Topic
                };
                return Json(result, JsonRequestBehavior.AllowGet);
            }
            return Json(null, JsonRequestBehavior.AllowGet);
        }

        // GET: Alert/CleanupAlerts
        [Authorize]
        public async Task<ActionResult> CleanupAlerts()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                int totalResolved = 0;
                
                // 1. Resolver alertas huérfanas (notificaciones cuyo sensor ya no existe)
                var orphanedAlerts = await _context.Alerts
                    .Where(a => !_context.Sensors.Any(s => s.SensorID == a.SensorID) && 
                           a.IsNotification && !a.IsResolved)
                    .ToListAsync();
                    
                foreach (var alert in orphanedAlerts)
                {
                    alert.IsResolved = true;
                    alert.ResolvedAt = DateTime.Now;
                    totalResolved++;
                }
                
                // 2. Resolver alertas antiguas (más de 24 horas)
                var oldThreshold = DateTime.Now.AddHours(-24);
                var oldAlerts = await _context.Alerts
                    .Where(a => a.IsNotification && !a.IsResolved && 
                           a.CreatedAt < oldThreshold)
                    .ToListAsync();
                    
                foreach (var alert in oldAlerts)
                {
                    alert.IsResolved = true;
                    alert.ResolvedAt = DateTime.Now;
                    totalResolved++;
                }
                
                // 3. Resolver alertas duplicadas (mismo sensor, no resueltas, excepto la más reciente)
                var activeSensors = await _context.Alerts
                    .Where(a => a.IsNotification && !a.IsResolved)
                    .GroupBy(a => a.SensorID)
                    .Select(g => new { SensorID = g.Key, Count = g.Count() })
                    .Where(x => x.Count > 1)
                    .ToListAsync();
                    
                foreach (var sensor in activeSensors)
                {
                    var sensorAlerts = await _context.Alerts
                        .Where(a => a.SensorID == sensor.SensorID && 
                               a.IsNotification && !a.IsResolved)
                        .OrderByDescending(a => a.CreatedAt)
                        .Skip(1) // Mantener la más reciente
                        .ToListAsync();
                        
                    foreach (var alert in sensorAlerts)
                    {
                        alert.IsResolved = true;
                        alert.ResolvedAt = DateTime.Now;
                        totalResolved++;
                    }
                }
                
                await _context.SaveChangesAsync();
                
                TempData["Success"] = $"Limpieza completada: {totalResolved} alertas resueltas";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en CleanupAlerts: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                TempData["Error"] = "Error al limpiar alertas: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // GET: Alert/DeleteAllNotifications
        [Authorize]
        public async Task<ActionResult> DeleteAllNotifications()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                
                // Obtener todas las notificaciones del usuario
                var notifications = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Where(a => 
                        a.GreenHouse.UserID == userId && 
                        a.IsNotification)
                    .ToListAsync();
                
                // Eliminar todas las notificaciones encontradas
                _context.Alerts.RemoveRange(notifications);
                await _context.SaveChangesAsync();
                
                TempData["Success"] = $"Se han eliminado {notifications.Count} notificaciones";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al eliminar notificaciones: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<ActionResult> GetNavbarNotifications()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                var alerts = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .Where(a => a.GreenHouse.UserID == userId && !a.IsResolved && a.IsNotification)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                // Devolver la vista parcial para el navbar
                return PartialView("_NotificationsPartial", alerts);
            }
            catch (Exception)
            {
                // En caso de error, devolver una vista parcial vacía
                return PartialView("_NotificationsPartial", new List<Alert>());
            }
        }
        
        [HttpGet]
        public async Task<ActionResult> GetNotificationsTable()
        {
            try
            {
                var userId = User.Identity.GetUserId();
                
                // Obtener todas las notificaciones del usuario
                var alerts = await _context.Alerts
                    .Include(a => a.GreenHouse)
                    .Include(a => a.Sensor)
                    .Where(a => a.GreenHouse.UserID == userId && a.IsNotification) // Solo notificaciones
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();
                
                Console.WriteLine($"Actualizando tabla de notificaciones. Notificaciones encontradas: {alerts.Count}");
                
                // Devolver la vista parcial solo con la tabla de notificaciones
                return PartialView("_NotificationsTablePartial", alerts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GetNotificationsTable: {ex.Message}");
                // En caso de error, devolver una vista parcial vacía
                return PartialView("_NotificationsTablePartial", new List<Alert>());
            }
        }
    }
} 