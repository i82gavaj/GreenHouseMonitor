-- Script de diagnóstico para alertas
-- Este script muestra información sobre las alertas en la base de datos

-- 1. Total de alertas en la base de datos
SELECT COUNT(*) AS TotalAlertas FROM Alerts;

-- 2. Alertas agrupadas por tipo
SELECT AlertType, COUNT(*) AS Cantidad 
FROM Alerts 
GROUP BY AlertType 
ORDER BY COUNT(*) DESC;

-- 3. Alertas por estado de resolución
SELECT IsResolved, COUNT(*) AS Cantidad 
FROM Alerts 
GROUP BY IsResolved;

-- 4. Alertas por tipo de notificación
SELECT IsNotification, COUNT(*) AS Cantidad 
FROM Alerts 
GROUP BY IsNotification;

-- 5. Alertas no resueltas que deberían mostrarse como notificaciones
SELECT a.AlertID, a.AlertType, a.Message, a.CreatedAt, a.CurrentValue, a.ThresholdRange,
       g.Name AS GreenHouseName, s.SensorName, s.Topic
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE a.IsResolved = 0 AND a.IsNotification = 1
ORDER BY a.CreatedAt DESC;

-- 6. Verificar configuraciones de alertas
SELECT a.AlertID, a.AlertType, a.ThresholdRange, a.IsNotification, a.IsResolved,
       g.Name AS GreenHouseName, s.SensorName, s.Topic
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE a.IsNotification = 0
ORDER BY a.AlertID;

-- 7. Verificar si hay alertas sin invernaderos o sensores válidos
SELECT a.AlertID, a.AlertType, a.Message, a.CreatedAt, a.IsNotification, a.IsResolved, 
       a.GreenHouseID, a.SensorID
FROM Alerts a
LEFT JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
LEFT JOIN Sensors s ON a.SensorID = s.SensorID
WHERE g.GreenHouseID IS NULL OR s.SensorID IS NULL;

-- 8. Verificar si hay alertas con valores anormalmente altos
SELECT a.AlertID, a.AlertType, a.CurrentValue, a.ThresholdRange, a.CreatedAt, 
       s.SensorName, g.Name AS GreenHouseName
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE 
  (a.AlertType = 0 AND a.CurrentValue > 100) OR  -- Temperatura
  (a.AlertType = 1 AND a.CurrentValue > 100) OR  -- Humedad
  (a.AlertType = 2 AND a.CurrentValue > 10000);   -- CO2

-- 9. Verificar alertas para un usuario específico
DECLARE @userId nvarchar(128) = (SELECT TOP 1 Id FROM AspNetUsers);
SELECT a.AlertID, a.AlertType, a.IsNotification, a.IsResolved, a.CreatedAt, a.Message,
       g.Name AS GreenHouseName, s.SensorName, u.Email
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
JOIN AspNetUsers u ON g.UserID = u.Id
WHERE g.UserID = @userId
ORDER BY a.CreatedAt DESC; 