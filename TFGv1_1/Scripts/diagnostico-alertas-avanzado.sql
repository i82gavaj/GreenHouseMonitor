-- Script de diagnóstico avanzado para problemas de alertas
-- Este script busca problemas específicos con alertas fantasma o encoladas

-- 1. Verificar la integridad referencial de las alertas
-- Buscar alertas que apunten a sensores o invernaderos que ya no existen
SELECT 
    a.AlertID, 
    a.GreenHouseID, 
    a.SensorID, 
    a.AlertType, 
    a.Message, 
    a.CreatedAt, 
    a.IsNotification, 
    a.IsResolved
FROM Alerts a
LEFT JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
LEFT JOIN Sensors s ON a.SensorID = s.SensorID
WHERE (g.GreenHouseID IS NULL OR s.SensorID IS NULL)
  AND a.IsResolved = 0
ORDER BY a.CreatedAt DESC;

-- 2. Verificar alertas huérfanas en el sistema
-- Buscar alertas sin relaciones válidas pero que siguen activas
SELECT 
    a.AlertID, 
    a.GreenHouseID, 
    a.SensorID, 
    a.CreatedAt,
    a.IsNotification,
    a.IsResolved,
    CASE WHEN g.GreenHouseID IS NULL THEN 'Invernadero no existe' ELSE '' END AS GreenHouseStatus,
    CASE WHEN s.SensorID IS NULL THEN 'Sensor no existe' ELSE '' END AS SensorStatus
FROM Alerts a
LEFT JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
LEFT JOIN Sensors s ON a.SensorID = s.SensorID
WHERE (g.GreenHouseID IS NULL OR s.SensorID IS NULL)
  AND a.IsResolved = 0;

-- 3. Buscar alertas que pertenecen a un usuario específico
-- Esto puede ayudar a identificar alertas que se muestran incorrectamente a un usuario
DECLARE @userId nvarchar(128) = (SELECT TOP 1 Id FROM AspNetUsers);
SELECT 
    u.UserName,
    a.AlertID, 
    a.AlertType, 
    a.Message, 
    a.CreatedAt, 
    a.IsNotification, 
    a.IsResolved,
    g.Name AS GreenHouseName,
    s.SensorName
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
JOIN AspNetUsers u ON g.UserID = u.Id
WHERE g.UserID = @userId
  AND a.IsNotification = 1
  AND a.IsResolved = 0
ORDER BY a.CreatedAt DESC;

-- 4. Verificar si hay alertas duplicadas (mismo sensor, tipo y valor)
SELECT 
    SensorID, 
    AlertType, 
    CurrentValue,
    IsResolved,
    COUNT(*) AS NumDuplicates,
    MIN(AlertID) AS MinAlertID,
    MAX(AlertID) AS MaxAlertID,
    MIN(CreatedAt) AS FirstCreated,
    MAX(CreatedAt) AS LastCreated
FROM Alerts
WHERE IsNotification = 1
  AND IsResolved = 0
GROUP BY SensorID, AlertType, CurrentValue, IsResolved
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC;

-- 5. Verificar si hay problemas con la consulta de alertas no resueltas principal
SELECT 
    a.AlertID, 
    a.AlertType,
    a.SensorID,
    g.UserID,
    a.CreatedAt,
    a.IsNotification,
    a.IsResolved
FROM Alerts a
JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE a.IsNotification = 1 
  AND a.IsResolved = 0
ORDER BY a.CreatedAt DESC;

-- 6. Verificar alertas que podrían haberse quedado en un estado inconsistente
SELECT 
    a.AlertID, 
    a.AlertType,
    a.SensorID,
    a.GreenHouseID,
    a.CreatedAt,
    a.IsNotification,
    a.IsResolved,
    a.ResolvedAt
FROM Alerts a
WHERE (a.IsResolved = 1 AND a.ResolvedAt IS NULL)    -- Inconsistencia: marcada como resuelta pero sin fecha
   OR (a.IsResolved = 0 AND a.ResolvedAt IS NOT NULL) -- Inconsistencia: no resuelta pero con fecha
ORDER BY a.CreatedAt DESC;

-- 7. Resolver todas las alertas no resueltas (¡CUIDADO! Solo ejecutar si es necesario)
/*
UPDATE Alerts
SET IsResolved = 1, ResolvedAt = GETDATE()
WHERE IsNotification = 1 AND IsResolved = 0;
*/

-- 8. Eliminar físicamente alertas huérfanas (¡CUIDADO! Solo ejecutar si es necesario)
/*
DELETE FROM Alerts
WHERE AlertID IN (
    SELECT a.AlertID
    FROM Alerts a
    LEFT JOIN GreenHouses g ON a.GreenHouseID = g.GreenHouseID
    LEFT JOIN Sensors s ON a.SensorID = s.SensorID
    WHERE (g.GreenHouseID IS NULL OR s.SensorID IS NULL)
);
*/ 